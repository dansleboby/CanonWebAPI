using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CanonSDK
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // --- Configuration ---
            // You can change the listening URL and port here.
            // "http://localhost:8080/" will only be accessible from your computer.
            // "http://*:8080/" will be accessible from other devices on your network.
            var listenerUrl = "http://localhost:8080/";

            // --- Initialization ---
            var cts = new CancellationTokenSource();
            var cameraController = new CanonController();

            Console.WriteLine("Canon Camera Webhook Server");
            Console.WriteLine("---------------------------");

            // --- Camera Setup ---
            try
            {
                Console.WriteLine("Initializing EDSDK and connecting to camera...");
                cameraController.Initialize();
                var cameraName = cameraController.GetCameraName();
                Console.WriteLine($"Successfully connected to: {cameraName}");
                Console.WriteLine("Make sure the camera is in a manual mode (like M, Av, Tv) for full control.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERROR: Could not initialize camera. {ex.Message}");
                Console.WriteLine("Please ensure a supported Canon camera is connected and turned on.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            // --- Webhook Server Setup ---
            var server = new WebhookServer(cameraController, listenerUrl);

            // Handle Ctrl+C press for graceful shutdown
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Shutting down...");
            };

            // --- Run Server ---
            try
            {
                Console.WriteLine($"\nWebhook server starting at {listenerUrl}");
                Console.WriteLine("Press Ctrl+C to stop the server.\n");
                await server.Start(cts.Token);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nServer error: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                // --- Shutdown ---
                Console.WriteLine("Closing camera session and terminating SDK...");
                cameraController.Dispose();
                Console.WriteLine("Shutdown complete.");
            }
        }
    }
}
