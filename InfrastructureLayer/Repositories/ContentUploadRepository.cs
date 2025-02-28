using DomainLayer.Interface;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Scoping;
using OfficeOpenXml;
using Umbraco.Cms.Core.Models;
using Umbraco.Extensions;
using DomainLayer.Models;
using System.Reflection;

namespace InfrastructureLayer.Repositories
{
    public class ContentUploadRepository : IContentUploadRepository
    {
        private readonly IContentService _contentService;
        private readonly IMediaService _mediaService;
        private readonly IUploadImageRepopsitory _uploadImageRepopsitory;

        public ContentUploadRepository(IContentService contentService, IMediaService mediaService, IUploadImageRepopsitory uploadImageRepopsitory)
        {
            _contentService = contentService;
            _mediaService = mediaService;
            _uploadImageRepopsitory = uploadImageRepopsitory;
        }
        public string ImportContentWithImage(List<string> filePaths, List<string> allowedImageTypes)
        {
            if (filePaths == null || filePaths.Count == 0) return "No files received.";

            var errors = new List<string>();

            foreach (var filePath in filePaths)
            {
                if (!System.IO.File.Exists(filePath))
                {
                    errors.Add($"File not found: {filePath}");
                    continue;
                }

                var fileExtension = Path.GetExtension(filePath)?.ToLower();
                string result = fileExtension switch
                {
                    ".xlsx" or ".xls" => ProcessExcelFile(filePath, allowedImageTypes), //Case syntex (=>):
                    ".csv" => ProcessCsvFile(filePath, allowedImageTypes),
                    _ => $"Unsupported file type: {fileExtension}" //_ (underscore) is the default case
                };

                if (result != "Success")
                {
                    errors.Add(result);
                }
            }

            return errors.Count == 0
                ? "All files processed successfully."
                : $"Some errors occurred: {string.Join("; ", errors)}";
        }
        

        // handle Excel file -> extension like xls, xlsx
        private string ProcessExcelFile(string filePath, List<string> allowedImageTypes)
        {
            var rowErrors = new List<string>();

            try
            {
                // To avoid commercial license warnings.
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using var package = new ExcelPackage(new FileInfo(filePath));
                var worksheet = package.Workbook.Worksheets[0];

                if (worksheet?.Dimension == null)
                    return "Invalid or empty Excel file.";

                int rowCount = worksheet.Dimension.End.Row;

                // Get home node
                var homeNode = _contentService.GetRootContent()
                                              .FirstOrDefault(x => x.ContentType.Alias == "homePage");
                if (homeNode == null)
                    return "Home Page not found";

                // Get first available Article node
                var articleNode = _contentService.GetPagedChildren(homeNode.Id, 0, int.MaxValue, out _)
                                                 .FirstOrDefault(x => x.ContentType.Alias == "article");
                if (articleNode == null)
                    return "Article Node Page not found";

                var existingArticles = _contentService.GetPagedChildren(articleNode.Id, 0, int.MaxValue, out _)
                                                      .Select(x => x.Name)
                                                      .ToHashSet(); // Optimize lookup

                for (int row = 2; row <= rowCount; row++) // skip headers
                {
                    var articleName = worksheet.Cells[row, 1].Text?.Trim();
                    var title = worksheet.Cells[row, 2].Text?.Trim();
                    var description = worksheet.Cells[row, 3].Text?.Trim();
                    var imagePath = worksheet.Cells[row, 4].Text?.Trim();

                    // Skip empty rows
                    if (string.IsNullOrEmpty(articleName) &&
                        string.IsNullOrEmpty(title) &&
                        string.IsNullOrEmpty(description) &&
                        string.IsNullOrEmpty(imagePath))
                    {
                        continue;
                    }

                    // Validate duplicate article name
                    if (existingArticles.Contains(articleName))
                    {
                        rowErrors.Add($"Duplicate article: {articleName} (Row {row})");
                        continue;
                    }

                    // Validate image type
                    string imageExtension = Path.GetExtension(imagePath)?.ToLower();
                    if (!allowedImageTypes.Contains(imageExtension))
                    {
                        rowErrors.Add($"Image extension '{imageExtension}'not found - at row {row}");
                        continue;
                    }

                    // Upload and get image ID
                    var imageId = _uploadImageRepopsitory.UploadAndGetImageId(imagePath);
                    var media = imageId.HasValue ? _mediaService.GetById(imageId.Value) : null;

                    if (imageId == null || media == null)
                    {
                        rowErrors.Add($"Image upload failed for row {row}");
                        continue;
                    }

                    // Create new content
                    var articleItem = _contentService.Create(articleName, articleNode.Id, "articleContent");
                    articleItem.SetValue("title", title);
                    articleItem.SetValue("description", description);
                    articleItem.SetValue("articleImage", media.GetUdi());

                    _contentService.Save(articleItem);
                    _contentService.SaveAndPublish(articleItem);
                    existingArticles.Add(articleName); // Prevent future duplicates in the same import
                }

                return rowErrors.Count == 0 ? "Success" : $"Some row errors: {string.Join(", ", rowErrors)}";
            }
            catch (Exception ex)
            {
                return $"Error processing Excel: {ex.Message}";
            }
        }

        // handle CSV file -> comma separated value 
        private string ProcessCsvFile(string filePath, List<string> allowedImageTypes)
        {
            var rowErrors = new List<string>();

            try
            {
                var lines = System.IO.File.ReadAllLines(filePath);
                if (lines.Length <= 1)
                    return "CSV file is empty or only contains headers.";

                // Get home node
                var homeNode = _contentService.GetRootContent()
                                              .FirstOrDefault(x => x.ContentType.Alias == "homePage");
                if (homeNode == null)
                    return "Home Page not found";

                // Get first available Article node
                var articleNode = _contentService.GetPagedChildren(homeNode.Id, 0, int.MaxValue, out _)
                                                 .FirstOrDefault(x => x.ContentType.Alias == "article");
                if (articleNode == null)
                    return "Article Node Page not found";

                // Cache existing articles for fast lookup
                var existingArticles = _contentService.GetPagedChildren(articleNode.Id, 0, int.MaxValue, out _)
                                                      .Select(x => x.Name)
                                                      .ToHashSet();

                // Process each row
                foreach (var line in lines.Skip(1)) // Skip header row
                {
                    var values = ParseCsvLine(line);
                    if (values.Count < 4) continue; // Ensure enough columns

                    var articleName = values[0].Trim();
                    var title = values[1].Trim();
                    var description = values[2].Trim();
                    var imagePath = values[3].Trim();

                    // Skip empty rows
                    if (string.IsNullOrEmpty(articleName) &&
                        string.IsNullOrEmpty(title) &&
                        string.IsNullOrEmpty(description) &&
                        string.IsNullOrEmpty(imagePath))
                    {
                        continue;
                    }

                    // Check for duplicate articles
                    if (existingArticles.Contains(articleName))
                    {
                        rowErrors.Add($"Duplicate article: {articleName} (Row {Array.IndexOf(lines, line) + 1})");
                        continue;
                    }

                    // Validate image type
                    var imageExtension = Path.GetExtension(imagePath)?.ToLower();
                    if (!allowedImageTypes.Contains(imageExtension))
                    {
                        rowErrors.Add($"Image extension '{imageExtension}'not found - at row {Array.IndexOf(lines, line) + 1}");
                        continue;
                    }

                    // Upload image
                    var imageId = _uploadImageRepopsitory.UploadAndGetImageId(imagePath);
                    var media = imageId.HasValue ? _mediaService.GetById(imageId.Value) : null;

                    if (imageId == null || media == null)
                    {
                        rowErrors.Add($"Image upload failed for row {Array.IndexOf(lines, line) + 1}");
                        continue;
                    }

                    // Create new content
                    var articleItem = _contentService.Create(articleName, articleNode.Id, "articleContent");
                    articleItem.SetValue("title", title);
                    articleItem.SetValue("description", description);
                    articleItem.SetValue("articleImage", media.GetUdi());

                    _contentService.Save(articleItem);
                    _contentService.SaveAndPublish(articleItem);
                    existingArticles.Add(articleName); // Prevent duplicate processing
                }

                return rowErrors.Count == 0 ? "Success" : $"Some row errors: {string.Join(", ", rowErrors)}";
            }
            catch (Exception ex)
            {
                return $"Error processing CSV: {ex.Message}";
            }
        }

        // splits a CSV row into a list of column values while handling commas inside quotes, trimming spaces, and preserving empty fields
        private List<string> ParseCsvLine(string line)
        {
            using var reader = new StringReader(line);

            //automatically handles commas inside quotes, trims spaces, and splits columns correctly.
            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(reader)
            {
                TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
                Delimiters = new[] { "," },

                //Ensures that values inside double quotes ("...") are treated as a single unit even if they contain commas.
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = true
            };

            return parser.ReadFields()?.ToList() ?? new List<string>();
        }


    }
}
