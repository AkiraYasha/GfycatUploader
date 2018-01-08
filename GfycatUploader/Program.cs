using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GfycatUploader
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            var gfycat = new GfycatClient();

            OpenFileDialog fileSelector = new OpenFileDialog
            {
                InitialDirectory = Directory.GetCurrentDirectory(),
            };

            var result = fileSelector.ShowDialog();
            if (result != DialogResult.OK)
            {
                Console.WriteLine("User aborted.");
                return;
            }
            else
            {

                Console.WriteLine("Selected File: " + fileSelector.FileName);

                using (var file = File.Open(fileSelector.FileName, FileMode.Open, FileAccess.Read))
                {
                    var gfyname = await gfycat.Publish(file,
                        new Progress<GfycatProgress>(report =>
                        {
                            Console.WriteLine("{0} {1}", report.Message, report.IsIndeterminate ? "..." : report.Progress.ToString());
                        }
                    ));

                    Console.WriteLine($"Opening 'https://gfycat.com/gifs/detail/{gfyname}' in default browser...");
                    Process.Start($"https://gfycat.com/gifs/detail/{gfyname}");
                }
            }

            Console.WriteLine("Complete, Press any key to exit.");
            Console.ReadKey();
        }
    }

    class GfycatClient
    {
        public async Task<String> Publish(Stream fileStream, IProgress<GfycatProgress> reporter)
        {
            // Using noMd5 here to skip gfycat's dupe check.
            // In the case of a dupe being detected, the status request will return back
            // the details about the dupe which is not currently handled.
            //
            // In real code, you can extend this to include things like titles and descriptions.
            // See api docs for more details.
            var createRequest = new GfycatCreateRequest()
            {
                noMd5 = true,
            };

            // Create the gfycat
            reporter.Report(new GfycatProgress("Creating gfycat"));
            var createResponse = await Requests.Create(createRequest);
            if (!createResponse.IsOk)
            {
                throw new InvalidOperationException("Something went wrong, send help.");
            }

            // Upload the file
            reporter.Report(new GfycatProgress("Uploading file"));
            Requests.Upload(createResponse.GfyName, fileStream);

            while (true)
            {
                var statusResponse = await Requests.Status(createResponse.GfyName);
                switch (statusResponse.Task)
                {
                    case "NotFoundo":
                        // This happening once is expected because there is a gap between
                        // when the file is uploaded, and when gfycat detects it.
                        //
                        // In real code, publish should fail if this happens too many times.
                        await Task.Delay(1000);
                        break;

                    case "encoding":
                        reporter.Report(new GfycatProgress("Encoding", statusResponse.Progress));
                        await Task.Delay(1000);
                        break;

                    case "complete":
                        reporter.Report(new GfycatProgress("Complete"));
                        return statusResponse.GfyName;

                    case "error":
                    default:
                        throw new InvalidOperationException("Something went wrong, send help.");
                }
            }
        }


        private static class Requests
        {
            private static readonly HttpClient client = new HttpClient();

            public static async Task<GfycatCreateResponse> Create(GfycatCreateRequest request)
            {
                var response = await client.PostAsJsonAsync("https://api.gfycat.com/v1/gfycats", request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsAsync<GfycatCreateResponse>();
            }

            public static async void Upload(string gfyname, Stream fileStream)
            {
                using (var formData = new MultipartFormDataContent())
                {
                    formData.Add(new StringContent(gfyname), "key", "key");
                    formData.Add(new StreamContent(fileStream), "file", gfyname);
                    var response = await client.PostAsync("https://filedrop.gfycat.com/", formData);

                    response.EnsureSuccessStatusCode();
                }
            }

            public static async Task<GfycatStatusResponse> Status(string gfyname)
            {
                var response = await client.GetAsync($"https://api.gfycat.com/v1/gfycats/fetch/status/{gfyname}");
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsAsync<GfycatStatusResponse>();
            }
        }

        private class GfycatCreateRequest
        {
            public bool noMd5 { get; set; }
        }

        private class GfycatCreateResponse
        {
            public bool IsOk { get; set; }
            public string GfyName { get; set; }
            public string Secret { get; set; }
            public string UploadType { get; set; }
        }

        private class GfycatStatusResponse
        {
            public string Task { get; set; }
            public string GfyName { get; set; }
            public double Progress { get; set; }
        }
    }

    public class GfycatProgress
    {
        public string Message { get; }
        public double Progress { get; }
        public bool IsIndeterminate
        {
            get => double.IsNegativeInfinity(Progress);
        }

        public GfycatProgress(string message, double progress = double.NegativeInfinity)
        {
            this.Message = message;
            this.Progress = progress;
        }
    }
}
