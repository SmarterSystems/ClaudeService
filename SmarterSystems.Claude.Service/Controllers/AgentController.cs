using Microsoft.AspNetCore.Mvc;
using SmarterSystems.Claude.Service.Controllers.Request;
using SmarterSystems.Claude.Service.Controllers.Response;
using SmarterSystems.Claude.Service.Services;

namespace SmarterSystems.Claude.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AgentController : ControllerBase
    {
        private readonly IClaudeCliRunner _runner;

        public AgentController(IClaudeCliRunner runner)
        {
            _runner = runner;
        }

        [HttpPost("file")]
        public async Task<FileResponse> UploadFile([FromBody] FileRequest request)
        {
            var response = new FileResponse();
            try
            {
                if (request?.File == null || request.File.Length == 0)
                    throw new InvalidOperationException("File is empty.");

                var extension = Path.GetExtension(request.FileName);
                if (string.IsNullOrWhiteSpace(extension))
                    extension = ExtensionFor(MimeTypes.Resolve(request.File, request.MediaType, request.FileName));

                var fileId = Guid.NewGuid().ToString("N") + extension;
                await System.IO.File.WriteAllBytesAsync(Path.Combine(_runner.WorkingDirectory, fileId), request.File);
                response.FileId = fileId;
            }
            catch (Exception ex)
            {
                response.HasError = true;
                response.Error = ex.Message;
            }
            return response;
        }

        [HttpPost("message")]
        public async Task<MessageResponse> SendMessage([FromBody] MessageRequest request, CancellationToken cancellationToken)
        {
            var response = new MessageResponse();
            try
            {
                if (request == null
                    || (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.FileId)))
                {
                    throw new InvalidOperationException("Message or FileId is required.");
                }
                if (string.IsNullOrWhiteSpace(request.Model))
                    throw new InvalidOperationException("Model is required.");

                var allowedTools = new List<string>();
                var prompt = request.Message ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(request.FileId))
                {
                    var path = Path.Combine(_runner.WorkingDirectory, request.FileId);
                    if (!System.IO.File.Exists(path))
                        throw new InvalidOperationException($"Unknown FileId '{request.FileId}'.");
                    allowedTools.Add("Read");
                    prompt = $"Read the file {path} and use it as the context for this request.\n\n{prompt}".Trim();
                }

                var result = await _runner.RunAsync(new ClaudeCliRequest
                {
                    Prompt = prompt,
                    Model = request.Model,
                    SessionId = request.SessionId == Guid.Empty ? null : request.SessionId.ToString(),
                    JsonSchema = request.Schema,
                    AllowedTools = allowedTools
                }, cancellationToken);

                response.Message = result.Text;
                response.StopReason = result.StopReason;
            }
            catch (Exception ex)
            {
                response.HasError = true;
                response.Error = ex.Message;
            }
            return response;
        }

        private static string ExtensionFor(string mediaType)
        {
            return mediaType switch
            {
                "application/pdf" => ".pdf",
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "application/json" => ".json",
                "text/csv" => ".csv",
                "text/html" => ".html",
                _ => ".txt"
            };
        }
    }
}
