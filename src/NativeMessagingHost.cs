using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace xTaskForge;

public static class NativeMessagingHost
{
    public static void RunConsoleLoop()
    {
        try
        {
            using var stdout = Console.OpenStandardOutput();
            using var stdin = Console.OpenStandardInput();

            byte[] lengthBytes = new byte[4];

            while (stdin.Read(lengthBytes, 0, 4) == 4)
            {
                int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                if (messageLength <= 0 || messageLength > 1024 * 1024) break;

                byte[] buffer = new byte[messageLength];
                int read = 0;
                while (read < messageLength)
                {
                    int chunkSize = stdin.Read(buffer, read, messageLength - read);
                    if (chunkSize <= 0) break;
                    read += chunkSize;
                }

                if (read < messageLength) break;

                string requestJson = Encoding.UTF8.GetString(buffer, 0, messageLength);
                string responseJson = SendToNamedPipe(requestJson);

                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
                byte[] responseLengthBytes = BitConverter.GetBytes(responseBytes.Length);

                stdout.Write(responseLengthBytes, 0, 4);
                stdout.Write(responseBytes, 0, responseBytes.Length);
                stdout.Flush();
            }
        }
        catch { }
    }

    private static string SendToNamedPipe(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "xTaskForgePipe", PipeDirection.InOut);
            client.Connect(2000); // 2 second timeout

            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            writer.WriteLine(message);
            string? response = reader.ReadLine();
            return response ?? "{\"success\":false,\"error\":\"Empty response from IPC server\"}";
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"IPC connection error: {ex.Message}" });
        }
    }
}
