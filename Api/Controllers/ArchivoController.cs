using System.Text;
using DevExtreme.AspNet.Data;
using SongStock.Data;
using SongStock.Api.Authorization;
using SongStock.Api.Utils;
using SongStock.Data.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using Newtonsoft.Json;

namespace SongStock.Api.Controllers
{
	[@Authorize]
	[ApiController]
	[Route("archivo")]
	// [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
	// CRUD => CREATE READ UPDATE DELETE
	public class ArchivoController : BaseController
	{
		private readonly DataContext _db;
		private readonly IConfiguration _config;
		private readonly IHttpContextAccessor _context;

		private readonly string _baseDir;

		public ArchivoController(DataContext context, IHttpContextAccessor httpContextAccessor, IConfiguration config)
		{
			_db = context;
			_config = config;
			_context = httpContextAccessor;
			_baseDir = _config["Path:FilesPath"];
		}

		[HttpGet("imagenes")] //archivo/imagen => CREATE - 
		public ActionResult Imagenes()
		{
			var res = Files.GetFiles(_baseDir + "img");
			return Ok(res);
		}

		[HttpGet("documentos")] //archivo/imagen => CREATE - 
		public ActionResult Documentos()
		{
			var res = Files.GetFiles(_baseDir + "doc");
			return Ok(res);
		}
	}
}
