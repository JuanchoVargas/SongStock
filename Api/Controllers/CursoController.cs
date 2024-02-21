using System.Text;
using DevExtreme.AspNet.Data;
using ESAP.Sirecec.Data;
using ESAP.Sirecec.Data.Api.Authorization;
using ESAP.Sirecec.Data.Api.Services;
using ESAP.Sirecec.Data.Api.Utils;
using ESAP.Sirecec.Data.Core;
using ESAP.Sirecec.Data.Enum;
using ESAP.Sirecec.Data.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ESAP.Sirecec.Data.Api.Controllers {
	[@Authorize]
	[ApiController]
	[Route("curso")]
	// [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
	// CRUD => CREATE READ UPDATE DELETE
	public class CursoController : BaseController {
		private readonly DataContext _db;
		private readonly ILogger _logger;
		private readonly IEmailService _email;
		private readonly IConfiguration _config;
		private readonly IHttpContextAccessor _context;

		private string Codigo(Curso item) {
			var code = "S4";
			code += item.DependenciaId == (int)Dependencia.Capacitacion ? "CA" : "AG";
			code = code + item.Id.ToString().PadLeft(6, '0');
			return code;
		}

		private List<Curso> Codigos() {
			var items = _db.Curso.Where(o => string.IsNullOrEmpty(o.Codigo)).ToList();
			foreach (Curso item in items) item.Codigo = Codigo(item);
			_db.SaveChanges();
			return items;
		}

		private List<Curso> CodigosTodos() {
			var items = _db.Curso.ToList();
			foreach (Curso item in items) item.Codigo = Codigo(item);
			_db.SaveChanges();
			return items;
		}

		public CursoController(DataContext context, IHttpContextAccessor httpContextAccessor,
			ILogger<CursoController> logger, IConfiguration config, IEmailService email) {
			_db = context;
			_logger = logger;
			_config = config;
			_email = email;
			_context = httpContextAccessor;
		}

		[HttpPost("ed")] // /api/curso/ed => CREATE - UPDATE
		public ActionResult Edit(Curso item) {
			Curso? curso = null;
			// Registro nuevo: CREATE
			if (item.Id == 0) {
				var obj = (Curso)item.CopyTo(new Curso());
				obj.CreadoPor = GetUserId();
				obj.CreadoEl = DateTime.Now;
				_db.Curso.Add(obj);
				_db.SaveChanges();
				Codigos();
				// Establece las fechas iniciales
				CursoFechaController.Fechas(obj, _db);
			} else {
				// Registro existente: EDIT
				var current = _db.Curso.First(o => o.Id == item.Id);
				if (current != null) {
					if (item.NumeroDias != current.NumeroDias)
						CursoFechaController.Fechas(item, _db);
					var final = (Curso)item.CopyTo(current);
					final.EditadoPor = GetUserId();
					final.EditadoEl = DateTime.Now;
					_db.SaveChanges();
					curso = final;
				}
			}
			return Ok(_db.Cursos.First(o => o.Id == curso.Id));
		}

		[HttpPost("codigos")]
		public ActionResult SetCodigos() {
			var res = CodigosTodos();
			return Ok(res);
		}

		[HttpPost("by-curso-id")]
		public ActionResult ByCursoId([FromBody] int cursoId) {
			var items = _db.CursoTema?.Where(o => o.CursoId == cursoId).ToList();
			return Ok(items);
		}

		[HttpPost("by-curso-id-participantes")]
		public ActionResult ByCursoIdParticipantes([FromBody] int cursoId) {
			var items = _db.CursoUsuario?.Where(o => o.CursoId == cursoId).ToList();
			return Ok(items);
		}

		[HttpPost("inscribir")] // /api/curso/inscribir
		public ActionResult Inscribir(CursoInscripcionModel item) {
			// 202402201051: Verifica que el curso exista
			var curso = _db.Cursos.FirstOrDefault(o => o.Id == item.CursoId);
			if (curso != null) {

				// 202402201051: Verifica que el usuario NO esté inscrito en el curso
				var inscrito = _db.CursoUsuario.Any(o => o.CursoId == item.CursoId && o.UsuarioId == item.UsuarioId);
				if (!inscrito) {

					// 202402201037: Obtiene el aula actual ordenando por su numero descendentemente (vista 'CursosAulas')
					var cupoPorAula = curso.CupoAula;
					var aula = _db.CursosAulas.Where(o => o.CursoId == item.CursoId).OrderByDescending(o => o.Aula).FirstOrDefault();

					// 202402201028: Lógica para el aula
					var numeroAulaActual = aula != null ? aula.Aula : 1; // Si NO hay aula aún, es la primera
					var inscritosEnAulaActual = aula != null ? aula.Inscritos : 0; // Si NO hay aula aún, no hay inscritos

					// Obtiene el # de aula dependiendo el # de inscritos
					var numeroDeAula = inscritosEnAulaActual <= cupoPorAula ? numeroAulaActual : (numeroAulaActual + 1);

					// 202402090054: Crea el registro
					// 202402201029: Se adiciona la propiedad 'Aula'
					var integrante = new CursoUsuario {
						CursoId = item.CursoId,
						UsuarioId = item.UsuarioId,
						Aula = numeroDeAula, // Suma nuevo integrante al aula
						CreadoPor = GetUserId(),
						CreadoEl = DateTime.Now,
					};
					_db.CursoUsuario.Add(integrante);
					_db.SaveChanges();

					// 202402201046: El cierre de inscripciones en el curso
					// se implementó en la vista 'Cursos',  propiedad 'InscripcionesAbiertas'

					// 202402151234: Crea las jornadas
					var jornadas = new List<string> { };
					if (curso.JornadaManana != null && (bool)curso.JornadaManana) jornadas.Add("m");
					if (curso.JornadaTarde != null && (bool)curso.JornadaTarde) jornadas.Add("t");
					if (curso.JornadaNoche != null && (bool)curso.JornadaNoche) jornadas.Add("n");

					// 202402151229: Crea las asistencias
					var lst = new List<CursoUsuarioAsistencia> { };
					var fechas = _db.CursoFecha.Where(o => o.CursoId == integrante.CursoId).ToList();
					foreach (var fecha in fechas) {
						foreach (var jornada in jornadas) {
							var cua = new CursoUsuarioAsistencia {
								CursoUsuarioId = integrante.Id,
								Fecha = fecha.FechaClase,
								Jornada = jornada,
								Asistio = false
							};
							lst.Add(cua);
						}
					}
					_db.CursoUsuarioAsistencia.AddRange(lst);
					_db.SaveChanges();

					// 202402090040: Envía el correo
					var usuario = _db.Usuarios.FirstOrDefault(o => o.Id == item.UsuarioId);
					if (usuario != null) {
						string body = @$"Hola {usuario.FirstName}!<br/><br/>
						Usted acaba de inscribirse al curso <b>{curso.Nombre}</b>.<br/><br/>
						Esta es la información que debe tener en cuenta:<br /><br />
						<b>Tipo de curso:</b> {curso.TipoCursoNombre.Capitalize()}<br />
						<b>Tipo de asistencia:</b> {curso.TipoAsistenciaNombre}<br />
						<b>Lugar de realización:</b> {curso.LugarRealizacion}<br />
						<b>Municipio:</b> {curso.MunicipioNombre}<br />
						<b>Departamento:</b> {curso.DepartamentoNombre}<br />
						<b>Inicio del curso:</b> {curso.FechaInicio:dd/MM/yyyy} {curso.HoraInicio:h:mm tt}<br />
						<b>Fin del curso:</b> {curso.FechaFin:dd/MM/yyyy}<br />
						<b>Responsable:</b> {curso.Responsable}<br />
						<b>Correo electrónico:</b> {curso.CorreoElectronico}<br />
						<br/>Muchas gracias!";
						_email.Send(usuario.Email, "SIRECEC 4.0 - INSCRIPCIÓN EXITOSA", body);
					}

				}
			}

			// Finaliza
			return Ok(item);
		}

		// En json: Front\public\data\config.json
		[HttpGet("estado/{cursoId}/{estadoId}")] // /api/curso/estado/15/423
		public ActionResult CursoEstado(int cursoId, int estadoId) {
			var obj = _db.Curso.FirstOrDefault(o => o.Id == cursoId);
			if (obj != null) {
				obj.EstadoCursoId = estadoId;
				obj.Publicado = estadoId == (int)EstadoCurso.Aprobado;
				obj.EditadoPor = GetUserId();
				obj.EditadoEl = DateTime.Now;
				_db.SaveChanges();
				return Ok(obj);
			}
			return Ok();
		}

		[HttpGet("cursos-por-usuario/{usuarioId}")] // /api/curso/por-usuario
		public ActionResult CursosPorUsuarioId(int usuarioId) {
			List<int> ids = new List<int>();
			var items = _db.CursoUsuario.Where(o => o.UsuarioId == usuarioId).ToList();
			if (items.Any()) {
				foreach (CursoUsuario item in items) ids.Add(item.CursoId);
				var res = _db.Cursos.Where(o => ids.Contains(o.Id)).ToList();
				return Ok(res);
			}
			return Ok(new List<CursoUsuario>());
		}

		[HttpPost("ed-tema")] // /api/curso/ed => CREATE - UPDATE
		public ActionResult EditTemas(CursoTemaModel curso) {
			var tId = curso.CursoId;
			var temasActuales = _db.CursoTema.Where(o => o.CursoId == tId).ToList();
			_db.CursoTema.RemoveRange(temasActuales);
			_db.SaveChanges();
			foreach (var item in curso.Temas) {
				var obj = (CursoTema)item.CopyTo(new CursoTema());
				obj.CreadoPor = GetUserId();
				obj.CreadoEl = DateTime.Now;
				_db.CursoTema.Add(obj);
				_db.SaveChanges();
			}
			return Ok();
		}

		[HttpPost("ed-encuesta")] // /api/curso/ed => CREATE - UPDATE
		public ActionResult EditEncuestas(CursoEncuestaModel curso) {
			var eId = curso.CursoId;
			var encuestasActuales = _db.CursoEncuesta.Where(o => o.CursoId == eId).ToList();
			_db.CursoEncuesta.RemoveRange(encuestasActuales);
			_db.SaveChanges();
			foreach (var item in curso.Encuestas) {
				var obj = (CursoEncuesta)item.CopyTo(new CursoEncuesta());
				obj.CreadoPor = GetUserId();
				obj.CreadoEl = DateTime.Now;
				_db.CursoEncuesta.Add(obj);
				_db.SaveChanges();
			}
			return Ok();
		}

		[HttpPost("ed-archivo")] // /api/curso/ed-archivo
		public ActionResult EditArchivo() {
			// Documento
			var str = Request.Form["documento"];
			var item = JsonConvert.DeserializeObject<CursoDocumento>(str);

			// Archivo
			var files = Request.Form.Files;
			if (files.Count > 0) {
				var file = files[0];
				var fileBase = _config["Path:FilesPath"];
				// var uniqueFileName = file.FileName.GetUniqueFileName();
				var ext = Path.GetExtension(Path.GetFileName(file.FileName)).ToLower();
				var uniqueFileName = Guid.NewGuid().ToString().ToLower() + ext;
				var uploadDir = Path.Combine(fileBase, ext == ".pdf" ? "doc" : "img");
				var filePath = Path.Combine(uploadDir, uniqueFileName);
				file.CopyTo(new FileStream(filePath, FileMode.Create));
				item.ArchivoNombre = uniqueFileName;
			}

			if (item.Id == 0) {
				var obj = (CursoDocumento)item.CopyTo(new CursoDocumento());
				obj.CreadoPor = GetUserId();
				obj.CreadoEl = DateTime.Now;
				_db.CursoDocumento.Add(obj);
				_db.SaveChanges();
				return Ok(obj);
			} else {
				var current = _db.CursoDocumento.First(o => o.Id == item.Id);
				if (current != null) {
					var final = (CursoDocumento)item.CopyTo(current);
					final.EditadoPor = GetUserId();
					final.EditadoEl = DateTime.Now;
					_db.SaveChanges();
					return Ok(final);
				}
				return Ok(item);
			}
		}

		[HttpPost("mail-test")] // /api/curso/mail-test
		[Authorization.AllowAnonymous]
		public ActionResult MailTest([FromBody] string email) {
			_email.Send(email, "SIRECEC 4.0 - PRUEBA", "Prueba de envío de correo.");
			return Ok();
		}

		[HttpGet("dep/{dependenciaId?}")] // /api/curso/dep/13 o 14
		[Authorization.AllowAnonymous]
		public ActionResult GetByDependenciaId(int? dependenciaId = null) {
			var items = _db.Cursos.Where(o => o.DependenciaId == dependenciaId);
			return Ok(items);
		}

		[HttpGet("all")] // /api/curso/all
		[Authorization.AllowAnonymous]
		public ActionResult GetAll() {
			var items = _db.Cursos.ToList();
			return Ok(items);
		}

		[HttpGet("published")] // /api/curso/published
		[Authorization.AllowAnonymous]
		public ActionResult GetPublished() {
			var items = _db.Cursos.Where(o => o.Publicado == true && o.InscripcionesAbiertas == true).ToList();
			return Ok(items);
		}

		[HttpGet("cursos-temas")]// api/curso/temas
		[Authorization.AllowAnonymous]
		public ActionResult CursosTemas() {
			var items = _db.CursosTemas?.ToList();
			return Ok(items);
		}

		[HttpGet("cursos-documentos")]// api/curso/temas
		[Authorization.AllowAnonymous]
		public ActionResult CursosDocumentos() {
			var items = _db.CursosDocumentos?.ToList();
			return Ok(items);
		}


		[HttpGet("{itemId?}")] // /api/curso/5 => CREATE - 
		[Authorization.AllowAnonymous]
		public ActionResult Get(int? itemId = null) {
			var item = _db.Cursos.FirstOrDefault(o => o.Id == itemId);
			return Ok(item);
		}

		[HttpPost("fechas")] // /curso/fechas
		[Authorization.AllowAnonymous]
		public ActionResult Fechas([FromBody] int cursoId) {
			var items = _db.CursoFecha?.Where(o => o.CursoId == cursoId).OrderBy(o => o.Id).ToList();
			return Ok(items);
		}

		[HttpPost("asistencia-guardar")]
		public ActionResult GuardarAsistencia(CursoAsistenciaModel item) {
			var cu = _db.CursoUsuario.FirstOrDefault(o => o.Id == item.cursoUsuario.Id);
			if (cu != null) {
				cu.Porcentaje = item.cursoUsuario.Porcentaje;
				cu.Aprueba = item.cursoUsuario.Aprueba;
				cu.EditadoPor = GetUserId();
				cu.EditadoEl = DateTime.Now;
				_db.SaveChanges();
			}
			var ca = _db.CursoUsuarioAsistencia.FirstOrDefault(o => o.Id == item.asistencia.Id);
			if (ca != null) {
				ca.Asistio = item.asistencia.Asistio;
				ca.EditadoPor = GetUserId();
				ca.EditadoEl = DateTime.Now;
				_db.SaveChanges();
			}
			return Ok();
		}

		[HttpPost("asistencias-guardar")]
		public ActionResult GuardarAsistencias(List<CursoAsistenciasModel> items) {
			if (items.Any()) {
				var existentes = _db.CursosAsistencias.Where(o => o.CursoId == items.First().cursoUsuario.CursoId).ToList();
				foreach (var item in items) {
					var current = _db.CursoUsuario.FirstOrDefault(o => o.Id == item.cursoUsuario.Id);
					if (current != null) {
						var final = (CursoUsuario)item.CopyTo(current);
						final.EditadoPor = GetUserId();
						final.EditadoEl = DateTime.Now;
						_db.SaveChanges();
					}
					// foreach (var asistencia in item.asistencias) {
					// 	// forea
					// }
					// var cu = (CursoUsuario)item.CopyTo(current);
					// final.EditadoPor = GetUserId();
					// final.EditadoEl = DateTime.Now;
					// item.cursoUsuario.EditadoPor = GetUserId();
					// final.EditadoEl = DateTime.Now;
					// _db.SaveChanges();
				}
			}
			// var items = _db.CursosAsistencias?.Where(o => o.CursoId == cursoId).ToList();
			return Ok();
		}

		[HttpPost("asistencia")]
		[Authorization.AllowAnonymous]
		public ActionResult Asistencia([FromBody] int cursoId) {
			var items = _db.CursosAsistencias?.Where(o => o.CursoId == cursoId).ToList();
			return Ok(items);
		}

		[HttpPost("archivos-dx")] // /api/curso/archivos-dx
		public ActionResult ArchivosDx() {
			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var data = JsonConvert.DeserializeObject<GenericModel>(opts.UserData);
			var items = _db.CursosDocumentos.Where(o => o.CursoId == data.id).OrderBy(o => o.Nombre).ToList();
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}

		[HttpPost("temas-dx")] // /api/curso/temas-dx
		public ActionResult TemasDx() {
			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var data = JsonConvert.DeserializeObject<GenericModel>(opts.UserData);
			var items = _db.CursosTemas.Where(o => o.CursoId == data.id).OrderBy(o => o.TemaNombre).ToList();
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}

		[HttpPost("participantes-dx")] // /api/curso/temas-dx
		public ActionResult ParticipantesDx() {
			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var data = JsonConvert.DeserializeObject<GenericModel>(opts.UserData);
			var items = _db.CursosUsuarios.Where(o => o.CursoId == data.id).OrderBy(o => o.Name).ToList();
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}

		[HttpPost("encuestas-dx")] // /api/curso/dx => DevExtreme DataGrid Get
		public ActionResult EncuestasDx() {
			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var data = JsonConvert.DeserializeObject<GenericModel>(opts.UserData);
			var items = _db.CursosEncuestas.Where(o => o.CursoId == data.id).OrderBy(o => o.Titulo).ToList();
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}

		[HttpPost("activos-dx")] // /api/curso/dx => DevExtreme DataGrid Get
		public ActionResult GetActivosDx() {

			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var items = _db.Cursos.Where(o => o.EstadoCursoId == (int)EstadoCurso.Aprobado
				|| o.EstadoCursoId == (int)EstadoCurso.Finalizado).OrderBy(o => o.Nombre).ToList();
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}

		[HttpPost("dx")] // /api/curso/dx => DevExtreme DataGrid Get
		public ActionResult GetDx() {
			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var items = _db.Cursos.OrderBy(o => o.Nombre).ToList();
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}

		[HttpPost("dx-bk")] // /api/curso/dx => DevExtreme DataGrid Get
		public ActionResult GetDxBk() {
			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var items = _db.Cursos.OrderBy(o => o.Nombre).ToList();
			// 202402091030: Filtra data
			JObject data = JsonConvert.DeserializeObject<JObject>(opts.UserData);
			if (data.ContainsKey("estadoId")) {
				var eId = (int)data["estadoId"];
				items = items.Where(o => o.EstadoCursoId == eId).ToList();
			}
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}
	}
}
