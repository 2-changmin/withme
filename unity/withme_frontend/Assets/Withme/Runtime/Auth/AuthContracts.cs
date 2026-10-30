using System;
using Newtonsoft.Json;

namespace Withme.Auth
{
    public sealed class AuthUser
    {
        [JsonProperty("id")] public string Id;
    }
    public sealed class AuthSession
    {
        [JsonProperty("access_token")] public string AccessToken;
        [JsonProperty("refresh_token")] public string RefreshToken;
        [JsonProperty("expires_at")] public long ExpiresAt;
        [JsonProperty("expires_in")] public long ExpiresIn;
        [JsonProperty("user")] public AuthUser User;

        public void Validate(long now)
        {
            if (string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(RefreshToken)
                || User == null || !Guid.TryParse(User.Id, out _))
                throw new ApiFailure("INVALID_RESPONSE", 0);
            if (ExpiresAt <= 0 && ExpiresIn > 0) ExpiresAt = now + ExpiresIn;
            if (ExpiresAt <= now) throw new ApiFailure("INVALID_RESPONSE", 0);
        }
    }
    public sealed class Pet
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("photoMediaId")] public string PhotoMediaId;
        [JsonProperty("createdAt")] public DateTimeOffset CreatedAt;
        [JsonProperty("updatedAt")] public DateTimeOffset UpdatedAt;
    }

    // Never carry response bodies, URLs or tokens in exceptions/logs.
    public sealed class ApiFailure : Exception
    {
        public string Code { get; }
        public int Status { get; }
        public bool Transient => Status >= 500 || Code == "NETWORK" || Code == "TIMEOUT";
        public ApiFailure(string code, int status) : base("Withme request failed")
        { Code = code; Status = status; }
    }

    public static class AuthMessages
    {
        public static string For(Exception exception)
        {
            if (exception is OperationCanceledException) return "요청을 취소했어요.";
            var error = exception as ApiFailure;
            if (error == null) return "잠시 문제가 생겼어요. 다시 시도해 주세요.";
            if (error.Transient) return "잠시 연결이 불안정해요. 다시 시도해 주세요.";
            switch (error.Code)
            {
                case "EMAIL_INVALID": return "이메일 주소를 다시 확인해 주세요.";
                case "CODE_INVALID": return "6자리 숫자 코드를 입력해 주세요.";
                case "otp_disabled": return "초대받은 이메일로 로그인할 수 있어요. 주소를 확인해 주세요.";
                case "otp_expired": return "코드가 맞지 않거나 시간이 지났어요. 새 코드를 받아 주세요.";
                case "UNAUTHORIZED": case "SESSION_EXPIRED": return "다시 로그인해 주세요.";
                case "FORBIDDEN": return "이 정보에 접근할 수 없어요.";
                case "NOT_FOUND": return "정보를 찾지 못했어요. 다시 확인해 주세요.";
                case "CONFLICT": return "이미 처리된 요청이에요. 현재 상태를 확인해 주세요.";
                case "VALIDATION_FAILED": return "입력한 내용을 다시 확인해 주세요.";
                case "STORAGE": return "로그인 정보를 안전하게 저장하지 못했어요. 다시 시도해 주세요.";
                case "CONFIGURATION": return "지금은 로그인에 연결할 수 없어요. 잠시 후 다시 시도해 주세요.";
            }
            if (error.Status == 429 || error.Code == "RESEND_WAIT" || error.Code == "LIMIT_EXCEEDED")
                return "잠시 후 다시 요청해 주세요.";
            return "요청을 처리하지 못했어요. 다시 시도해 주세요.";
        }
    }
}
