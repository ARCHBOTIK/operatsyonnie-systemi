using System;
using System.Collections.Generic;
using System.Text;

namespace SecurePassword.SQL_Rus_.NetworkProtocols
{
    internal class TcpBridge
    {
        public async Task pushProfile()
        {
            var client = new DuplexTCPClient();
            try
            {
                await client.ConnectAsync("0.0.0.0", 8192);
                Console.WriteLine("Connected to server");
                string[] files = {
                    Path.Combine(FileSystem.AppDataDirectory, "file1.json"), //тут надо будет указать окончательные имена файлов, где лежат данные
                    Path.Combine(FileSystem.AppDataDirectory, "file2.json"),
                    Path.Combine(FileSystem.AppDataDirectory, "file3.json"),
                    Path.Combine(FileSystem.AppDataDirectory, "file4.json")
                };
                foreach (string filepath in files)
                {
                    if (!File.Exists(filepath))
                    {
                        Console.WriteLine($"File {filepath} was not found");
                        continue;
                    }
                    byte[] data = File.ReadAllBytes(filepath);
                    await client.SendDataAsync(data);
                    Console.WriteLine($"Data from file {filepath} was sent successfully, size: {data.Length}");
                    await Task.Delay(100);
                }
                Console.WriteLine("Files sent successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in sending sequence: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine("Connection closed");
            }
        }
    }
}
