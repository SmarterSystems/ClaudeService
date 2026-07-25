namespace SmarterSystems.Claude.Service.Services
{
    public static class MimeTypes
    {
        public static string Resolve(byte[] data, string explicitType, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(explicitType))
                return explicitType.Trim().ToLowerInvariant();

            var byExtension = FromExtension(fileName);
            if (byExtension != null) return byExtension;

            var bySniff = Sniff(data);
            if (bySniff != null) return bySniff;

            throw new InvalidOperationException(
                "Media type could not be determined. Provide FileRequest.MediaType or FileName with an extension.");
        }

        private static string FromExtension(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".txt" or ".md" or ".log" => "text/plain",
                ".json" => "application/json",
                ".csv" => "text/csv",
                ".html" or ".htm" => "text/html",
                _ => null
            };
        }

        private static string Sniff(byte[] d)
        {
            if (d == null || d.Length < 4) return null;
            if (d[0] == 0x25 && d[1] == 0x50 && d[2] == 0x44 && d[3] == 0x46) return "application/pdf";
            if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
            if (d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
            if (d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return "image/gif";
            if (d.Length >= 12 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46
                && d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50) return "image/webp";
            return null;
        }
    }
}
