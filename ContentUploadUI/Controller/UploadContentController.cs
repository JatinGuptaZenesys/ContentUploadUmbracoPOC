using DomainLayer.Interface;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;

namespace ContentUploadUI.Controller
{
    public class UploadContentController : SurfaceController
    {
        private readonly IContentUploadRepository _contentUploadRepository;

        public UploadContentController(IUmbracoContextAccessor umbracoContextAccessor,
            IUmbracoDatabaseFactory databaseFactory,
            ServiceContext services,
            AppCaches appCaches,
            IProfilingLogger profilingLogger,
            IPublishedUrlProvider publishedUrlProvider,
            IContentUploadRepository contentUploadRepository) : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
        {
            _contentUploadRepository = contentUploadRepository;
        }

        [HttpPost]
        public IActionResult ImportContentWithImage(List<IFormFile> fileUploads, List<string> imageTypes)
        {
            if (fileUploads == null || fileUploads.Count == 0)
            {
                return BadRequest("No file selected.");
            }

            // This list will hold the file paths of temporarily saved uploaded files.
            List<string> tempFilePaths = new List<string>();
            try
            {
                foreach (var fileUpload in fileUploads)
                {
                    string fileExtension = Path.GetExtension(fileUpload.FileName);
                    string tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + fileExtension);

                    using (var stream = new FileStream(tempFilePath, FileMode.Create)) //Creates a new file in the temp folder.
                    {
                        fileUpload.CopyTo(stream); //Writes the uploaded file's data into the newly created file.
                    }
                    tempFilePaths.Add(tempFilePath);
                }

                //Call repository method with the uploaded file path
                var result = _contentUploadRepository.ImportContentWithImage(tempFilePaths, imageTypes);

                if (result == "Excel data imported successfully.")
                {
                    return Ok(result);
                }

                return BadRequest(result);
            }
            finally
            {
                foreach (var filePath in tempFilePaths)
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
            }
        }

    }
}