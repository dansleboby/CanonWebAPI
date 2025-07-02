using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Collections.Generic;
using EDSDKLib;

namespace CanonSDK
{
    public class WebhookServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly CanonController _cameraController;

        public WebhookServer(CanonController cameraController, string url)
        {
            if (!HttpListener.IsSupported)
            {
                throw new NotSupportedException("HttpListener is not supported on this platform.");
            }
            _listener.Prefixes.Add(url);
            _cameraController = cameraController;
        }

        public async Task Start(CancellationToken token)
        {
            _listener.Start();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.Url.AbsolutePath.ToLower() == "/videostream")
                    {
                        // Handle the video stream in a separate task
                        _ = Task.Run(() => StreamVideo(context, token), token);
                    }
                    else
                    {
                        // Handle all other API requests
                        _ = Task.Run(() => ProcessRequest(context), token);
                    }
                }
                catch (HttpListenerException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Listener error: {ex.Message}");
                    Console.ResetColor();
                }
            }
            _listener.Stop();
        }

        private async Task StreamVideo(HttpListenerContext context, CancellationToken token)
        {
            var response = context.Response;
            var boundary = "frame";
            response.ContentType = $"multipart/x-mixed-replace; boundary=--{boundary}";
            response.SendChunked = true;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var frameData = _cameraController.GetLiveView();

                    var headerString =
                        $"--{boundary}\r\n" +
                        "Content-Type: image/jpeg\r\n" +
                        $"Content-Length: {frameData.Length}\r\n\r\n";

                    var headerBytes = Encoding.UTF8.GetBytes(headerString);

                    // The 'try...catch' block handles client disconnections.
                    // If the client closes the connection, WriteAsync will throw an exception.
                    try
                    {
                        await response.OutputStream.WriteAsync(headerBytes, 0, headerBytes.Length, token);
                        await response.OutputStream.WriteAsync(frameData, 0, frameData.Length, token);
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), 0, 2, token);
                    }
                    catch (Exception)
                    {
                        // Client disconnected, break the loop to end the stream for this client.
                        break;
                    }

                    await Task.Delay(50, token);
                }
            }
            catch (TaskCanceledException)
            {
                // This is expected when the server is shutting down.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Video stream error: {ex.Message}");
            }
            finally
            {
                response.OutputStream.Close();
            }
        }


        private async void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var responseString = "";
            var statusCode = 200;
            var contentType = "application/json";

            try
            {
                Console.WriteLine($"Received {request.HttpMethod} request for {request.Url.AbsolutePath}");
                var route = request.Url.AbsolutePath.ToLower();

                switch (route)
                {
                    case "/":
                        responseString = GetApiHelp();
                        contentType = "text/plain";
                        break;
                    case "/takepicture":
                        //if (request.HttpMethod == "POST")
                        {
                            var filePath = _cameraController.TakePicture();
                            responseString = $"{{\"status\":\"success\", \"message\":\"Image saved.\", \"filePath\":\"{filePath.Replace("\\", "\\\\")}\"}}";
                        }
                        //else { statusCode = 405; }
                        break;
                    case "/liveview": // This can still provide a single frame if needed
                        if (request.HttpMethod == "GET")
                        {
                            var imageData = _cameraController.GetLiveView();
                            response.ContentType = "image/jpeg";
                            response.ContentLength64 = imageData.Length;
                            await response.OutputStream.WriteAsync(imageData, 0, imageData.Length);
                            response.OutputStream.Close();
                            return;
                        }
                        else { statusCode = 405; }
                        break;
                    case "/iso":
                    case "/aperture":
                    case "/shutterspeed":
                    case "/whitebalance":
                        if (request.HttpMethod == "GET")
                        {
                            responseString = HandleGetSetting(route);
                        }
                        else if (request.HttpMethod == "POST")
                        {
                            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                            {
                                var requestBody = await reader.ReadToEndAsync();
                                responseString = HandleSetSetting(route, requestBody);
                            }
                        }
                        else { statusCode = 405; }
                        break;
                    default:
                        statusCode = 404;
                        responseString = $"{{\"status\":\"error\", \"message\":\"Endpoint not found.\"}}";
                        break;
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseString = $"{{\"status\":\"error\", \"message\":\"{ex.Message}\"}}";
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error processing {context.Request.Url}: {ex.Message}");
                Console.ResetColor();
            }

            if (statusCode == 405) responseString = $"{{\"status\":\"error\", \"message\":\"Method not allowed for this endpoint.\"}}";

            response.StatusCode = statusCode;
            response.ContentType = contentType;
            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private string HandleGetSetting(string route)
        {
            uint value = 0;
            Dictionary<uint, string> list = null;

            switch (route)
            {
                case "/iso":
                    value = _cameraController.GetSetting(EDSDK.PropID_ISOSpeed, out list);
                    break;
                case "/aperture":
                    value = _cameraController.GetSetting(EDSDK.PropID_Av, out list);
                    break;
                case "/shutterspeed":
                    value = _cameraController.GetSetting(EDSDK.PropID_Tv, out list);
                    break;
                case "/whitebalance":
                    value = _cameraController.GetSetting(EDSDK.PropID_WhiteBalance, out list);
                    break;
            }

            var currentValStr = "N/A";
            if (list.ContainsKey(value))
            {
                currentValStr = list[value];
            }
            var availableValuesJson = new StringBuilder("{");
            if (list != null)
            {
                foreach (var kvp in list)
                {
                    availableValuesJson.Append($"\"{kvp.Key}\":\"{kvp.Value}\",");
                }
                if (list.Count > 0) availableValuesJson.Length--;
            }
            availableValuesJson.Append("}");

            return $"{{\"current_value\":\"{value}\", \"current_value_str\":\"{currentValStr}\", \"available_values\":{availableValuesJson.ToString()}}}";
        }

        private string HandleSetSetting(string route, string body)
        {
            var valueStr = body.Split(':')[1].Replace("}", "").Replace("\"", "").Trim();
            if (!uint.TryParse(valueStr, out var value))
            {
                throw new ArgumentException("Invalid 'value' in request body. Must be an unsigned integer.");
            }

            switch (route)
            {
                case "/iso": _cameraController.SetSetting(EDSDK.PropID_ISOSpeed, value); break;
                case "/aperture": _cameraController.SetSetting(EDSDK.PropID_Av, value); break;
                case "/shutterspeed": _cameraController.SetSetting(EDSDK.PropID_Tv, value); break;
                case "/whitebalance": _cameraController.SetSetting(EDSDK.PropID_WhiteBalance, value); break;
            }

            return $"{{\"status\":\"success\", \"message\":\"Setting updated successfully.\"}}";
        }

        private string GetApiHelp()
        {
            return @"
Canon Camera Webhook API Help

Available Endpoints:

[GET] /videostream
    - Provides a continuous Motion JPEG (MJPEG) stream from the camera's live view.
    - Response: A multipart/x-mixed-replace HTTP stream.

[POST] /takepicture
    - Triggers the camera to take a picture and saves it to the local disk.
    - Response: JSON with file path.

[GET] /liveview
    - Returns a single live view frame from the camera.
    - Response: JPEG image data.

[GET] /iso
[GET] /aperture
[GET] /shutterspeed
[GET] /whitebalance
    - Gets the current value and a list of available values for the specified setting.
    - Response: JSON with current value and available options.

[POST] /iso
[POST] /aperture
[POST] /shutterspeed
[POST] /whitebalance
    - Sets a new value for the specified setting.
    - Request Body: JSON -> { ""value"": <uint> }
    - Response: JSON with success/error message.
";
        }
    }
}