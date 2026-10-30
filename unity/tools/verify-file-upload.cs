using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Withme.Auth;
using Withme.Registration;

public static class VerifyFileUpload
{
    static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    static async Task<object> Case(int status, bool cancel, byte[] expected, PhotoDraft photo)
    {
        var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        using (var lifetime = new CancellationTokenSource())
        using (var cancellation = new CancellationTokenSource())
        {
            var received = new TaskCompletionSource<bool>(); string headers = null; byte[] body = null;
            var listening = Task.Run(async () =>
            {
                using (var client = await server.AcceptTcpClientAsync())
                using (var stream = client.GetStream())
                {
                    var head = new MemoryStream(); var one = new byte[1];
                    while (head.Length < 16384)
                    {
                        if (await stream.ReadAsync(one, 0, 1, lifetime.Token) != 1) throw new IOException();
                        head.WriteByte(one[0]);
                        var bytes = head.GetBuffer(); var n = (int)head.Length;
                        if (n >= 4 && bytes[n-4] == 13 && bytes[n-3] == 10 && bytes[n-2] == 13 && bytes[n-1] == 10) break;
                    }
                    headers = Encoding.ASCII.GetString(head.ToArray());
                    var lengthLine = headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Single(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    body = new byte[int.Parse(lengthLine.Substring(lengthLine.IndexOf(':') + 1).Trim())];
                    var offset = 0;
                    while (offset < body.Length)
                    {
                        var n = await stream.ReadAsync(body, offset, body.Length - offset, lifetime.Token);
                        if (n == 0) throw new IOException(); offset += n;
                    }
                    received.TrySetResult(true);
                    if (cancel) await Task.Delay(10000, lifetime.Token);
                    var reply = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Result\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(reply, 0, reply.Length, lifetime.Token);
                }
            });
            try
            {
                var ticket = new UploadTicket { MediaId = Guid.NewGuid().ToString(), UploadUrl = "http://127.0.0.1:" + port + "/fixture", Headers = new Dictionary<string, string> { ["Content-Type"] = "image/png", ["x-upload-fixture"] = "yes" } };
                var upload = new UnityFileUploader(true).UploadAsync(ticket, photo, null, cancellation.Token);
                if (cancel)
                {
                    var ready = await Task.WhenAny(received.Task, Task.Delay(10000)); Check(ready == received.Task, "No file received");
                    cancellation.Cancel();
                    try { await upload; throw new Exception("Cancellation missing"); } catch (OperationCanceledException) { }
                }
                else if (status == 403)
                {
                    try { await upload; throw new Exception("Expiry missing"); } catch (ApiFailure e) { Check(e.Code == "UPLOAD_EXPIRED", "Wrong expiry"); }
                }
                else await upload;
                Check(body != null && body.SequenceEqual(expected), "Raw file body changed");
                Check(headers.StartsWith("PUT /fixture HTTP/1.1") && headers.Contains("image/png") && headers.Contains("x-upload-fixture: yes"), "Returned headers not used");
                Check(!headers.Contains("Authorization:") && !headers.Contains("Cookie:"), "Credentials leaked to storage");
                return new { status, cancelled = cancel, exactBytes = body.Length, noAuthHeaders = true };
            }
            finally
            {
                lifetime.Cancel(); server.Stop();
                try { await listening; } catch (OperationCanceledException) { } catch (IOException) { }
            }
        }
    }
    public static async Task<object> Run()
    {
        var path = Path.Combine(Application.temporaryCachePath, "withme-upload-bytes.bin");
        var bytes = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray(); File.WriteAllBytes(path, bytes);
        var photo = new PhotoDraft { LocalPath = path, SizeBytes = bytes.Length, MimeType = "image/png" };
        try
        {
            return new { passed = 3, cases = new[] { await Case(200, false, bytes, photo), await Case(403, false, bytes, photo), await Case(200, true, bytes, photo) }, productionWrites = 0 };
        }
        finally { File.Delete(path); }
    }
}
