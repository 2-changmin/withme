using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Withme.Auth;

namespace Withme.Character
{
    public sealed class RigSession
    {
        public int Version = 1;
        public string UserId, ModelId, JobId;
        public RigJoints Joints, PendingJoints;
        public bool Attempted;
        public void Validate(string user, string model)
        {
            if (Version != 1 || UserId != user || ModelId != model || !Guid.TryParse(user, out _)) throw new ApiFailure("CHECKPOINT_INVALID", 0);
            RouteId(ModelId); if (JobId != null) RouteId(JobId);
            Joints?.Validate(); PendingJoints?.Validate();
            if (Attempted && PendingJoints == null) throw new ApiFailure("CHECKPOINT_INVALID", 0);
        }
        public static string RouteId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 200 || !Regex.IsMatch(id, "^[a-zA-Z0-9_-]+$")) throw new ApiFailure("INVALID_RESPONSE", 0);
            return id;
        }
    }
    public interface IRigStore { RigSession Read(string user, string model); void Save(RigSession value); }
    public sealed class RigStore : IRigStore
    {
        readonly string directory;
        public RigStore(string directory) { this.directory = directory; }
        string PathFor(string user, string model)
        {
            if (!Guid.TryParse(user, out _)) throw new ApiFailure("CHECKPOINT_INVALID", 0);
            RigSession.RouteId(model);
            using (var sha = SHA256.Create()) return Path.Combine(directory, user + "-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(model))).Replace("-", "").ToLowerInvariant() + ".json");
        }
        public RigSession Read(string user, string model)
        {
            var path = PathFor(user, model); if (!File.Exists(path)) return null;
            try { var s = JsonConvert.DeserializeObject<RigSession>(File.ReadAllText(path)); if (s == null) throw new ApiFailure("CHECKPOINT_INVALID", 0); s.Validate(user, model); return s; }
            catch (Exception) { throw new ApiFailure("CHECKPOINT_INVALID", 0); }
        }
        public void Save(RigSession value)
        {
            value.Validate(value.UserId, value.ModelId);
            try
            {
                Directory.CreateDirectory(directory); var path = PathFor(value.UserId, value.ModelId); var temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(value));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (Exception) { throw new ApiFailure("STORAGE", 0); }
        }
    }
}
