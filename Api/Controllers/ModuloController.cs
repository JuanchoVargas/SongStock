using System.Text;
using DevExtreme.AspNet.Data;
using ESAP.Sirecec.Data;
using ESAP.Sirecec.Data.Api.Authorization;
using ESAP.Sirecec.Data.Api.Utils;
using ESAP.Sirecec.Data.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Newtonsoft.Json;

namespace ESAP.Sirecec.Data.Api.Controllers
{
	[@Authorize]
	[ApiController]
	[Route("modulo")]
	// [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
	// CRUD => CREATE READ UPDATE DELETE
	public class ModuloController : BaseController
	{
		private readonly DataContext _db;
		private readonly IHttpContextAccessor _context;

		public ModuloController(DataContext context, IHttpContextAccessor httpContextAccessor)
		{
			_db = context;
			_context = httpContextAccessor;
		}

		[HttpPost("ed")] // /api/curso/ed => CREATE - UPDATE
		public ActionResult Edit(Modulo item)
		{
			var u = User;

			// Registro nuevo: CREATE
			if (item.Id == 0)
			{
				var obj = (Modulo)item.CopyTo(new Modulo());
				obj.CreadoPor = GetUserId();
				obj.CreadoEl = DateTime.Now;
				_db.Modulo.Add(obj);
				_db.SaveChanges();
				return Ok(obj);
			}
			else
			{
				// Registro existente: EDIT
				var current = _db.Modulo.First(o => o.Id == item.Id);
				if (current != null)
				{
					var final = (Modulo)item.CopyTo(current);
					final.EditadoPor = GetUserId();
					final.EditadoEl = DateTime.Now;
					_db.SaveChanges();
					return Ok(final);
				}

				return Ok(item);
			}
		}

		[HttpGet("{itemId?}")] // /api/curso/5 => CREATE - 
		[Authorization.AllowAnonymous]
		public ActionResult Get(int? itemId = null)
		{
			var item = _db.Modulo.FirstOrDefault(o => o.Id == itemId);
			return Ok(item);
		}

		[HttpGet("all")]
		public ActionResult GetAll()
		{
			var items = _db.Modulo?.ToList();
			return Ok(items);
		}

		[HttpPost("dx")] // /api/curso/dx => DevExtreme DataGrid Get
		public ActionResult GetDx()
		{
			StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8);
			var str = reader.ReadToEndAsync().Result;
			var opts = JsonConvert.DeserializeObject<LoadOptions>(str);
			opts.PrimaryKey = new[] { "Id" };
			var items = _db.Modulo.OrderBy(o => o.Nombre).ToList();
			var loadResult = DataSourceLoader.Load(items, opts);
			return Ok(loadResult);
		}
	}
}
