using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Withme.Auth;

namespace Withme.Generation
{
    public sealed class GenerationJob
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("type")] public string Type;
        [JsonProperty("status")] public string Status;
        [JsonProperty("progress")] public int Progress;
        [JsonProperty("result")] public JObject Result;
        [JsonProperty("errorCode")] public string ErrorCode;
    }
    public sealed class AcceptedJob { [JsonProperty("jobId")] public string JobId; }
    public sealed class GenerationSession
    {
        public int Version = 1;
        public string UserId, PetId, PetName, ReferenceJobId, ModelJobId, ReferenceId, ModelId;
        public string[] MediaIds;
        public string PendingAction, PendingKey, PendingReferenceId;
        public int Retries;
        public string Stage = "reference";
        public void Validate(string user)
        {
            if (Version != 1 || UserId != user || !Guid.TryParse(UserId, out _) || !Guid.TryParse(PetId, out _)
                || MediaIds == null || MediaIds.Length < 1 || MediaIds.Length > 4 || MediaIds.Any(id => !Guid.TryParse(id, out _))
                || (Stage != "reference" && Stage != "review" && Stage != "model" && Stage != "ready")
                || Retries < 0 || Retries > 3) throw new ApiFailure("CHECKPOINT_INVALID", 0);
            if (PendingAction != null && ((PendingAction != "create" && PendingAction != "retry" && PendingAction != "approve") || !Guid.TryParse(PendingKey, out _)))
                throw new ApiFailure("CHECKPOINT_INVALID", 0);
        }
    }
    public interface IGenerationStore { GenerationSession Read(string user); void Save(GenerationSession session); void Delete(string user); }
    public sealed class GenerationStore : IGenerationStore
    {
        readonly string directory;
        public GenerationStore(string directory) { this.directory = directory; }
        string PathFor(string user)
        {
            if (!Guid.TryParse(user, out _)) throw new ApiFailure("CHECKPOINT_INVALID", 0);
            return Path.Combine(directory, user + ".json");
        }
        public GenerationSession Read(string user)
        {
            var path = PathFor(user); if (!File.Exists(path)) return null;
            try { var value = JsonConvert.DeserializeObject<GenerationSession>(File.ReadAllText(path)); if (value == null) throw new ApiFailure("CHECKPOINT_INVALID", 0); value.Validate(user); return value; }
            catch (ApiFailure) { throw; }
            catch (Exception) { throw new ApiFailure("CHECKPOINT_INVALID", 0); }
        }
        public void Save(GenerationSession value)
        {
            value.Validate(value.UserId);
            try
            {
                Directory.CreateDirectory(directory); var path = PathFor(value.UserId); var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(value));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (Exception) { throw new ApiFailure("STORAGE", 0); }
        }
        public void Delete(string user) { var path = PathFor(user); if (File.Exists(path)) File.Delete(path); }
    }
}
