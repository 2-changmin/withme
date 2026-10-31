using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Withme.Character
{
    public sealed class StandardRigProbe : MonoBehaviour
    {
        public StandardRigPlayback[] players;
        public TMP_Text status;
        public bool Ready { get; private set; }
        public string Mode { get; private set; } = "shared";
        public string Clip { get; private set; } = "Idle";
        async void Start()
        {
            status.text = "검토용 리깅 모델 불러오는 중";
            foreach (var player in players)
            {
                await player.LoadAsync(player.fixtureName);
                if (!player.IsReady) { status.text = player.LastError; return; }
            }
            Ready = true; Play("Idle");
        }
        public void Play(string name)
        {
            if (!Ready) return;
            foreach (var player in players) player.Play(name); Clip = name;
            status.text = $"검토용 · 품질 승인 전\n{name} · {(Mode == "shared" ? "공용 Generic + 체형 보정" : "모델별 클립")}";
        }
        public void ToggleSource()
        {
            Mode = Mode == "shared" ? "own" : "shared";
            foreach (var player in players) player.useShared = Mode == "shared";
            Play(Clip);
        }
        public void TogglePause() { foreach (var player in players) player.Paused = !player.Paused; }
    }
}
