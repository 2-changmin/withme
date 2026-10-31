using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Withme
{
    // 강아지 효과음 재생. 강아지 GameObject에 붙인다.
    // Resources/Sounds/Dog/ 아래(하위 폴더 포함) 소리를 이름 앞부분으로 묶는다: greet_01, greet_02 → "greet"
    //   동작: greet(반기기) · pet(쓰다듬기) · fetch(공 쫓기) · idle(대기 반복)
    //   생활: sniff · lick · yawn · shake (대기 중 무작위) · steps (걷기 Animation Event)
    //   크기: medium · large (barkSize를 정하면 greet·fetch 대신 재생)
    public sealed class DogSounds : MonoBehaviour
    {
        const string Folder = "Sounds/Dog";
        static readonly string[] LifeSets = { "sniff", "lick", "yawn", "shake" };

        [Tooltip("비우면 소형견. \"medium\" 또는 \"large\"면 짖음을 그 크기 소리로 바꾼다")]
        public string barkSize = "";
        [Range(0f, 0.2f)] public float pitchJitter = 0.05f;
        public Vector2 lifeInterval = new(10f, 30f);

        Dictionary<string, AudioClip[]> sets;
        readonly Dictionary<string, AudioClip> last = new();
        AudioSource oneShot, loop;
        Coroutine life;

        // 음소거(F13). 설정 화면에서 바꾼다.
        public bool Muted
        {
            get => oneShot.mute;
            set { oneShot.mute = loop.mute = value; if (value) StopAll(); }
        }
        public int LoadedClipCount => sets?.Sum(p => p.Value.Length) ?? 0;
        public bool IsPlaying => oneShot.isPlaying || loop.isPlaying;

        void Awake()
        {
            sets = Resources.LoadAll<AudioClip>(Folder)
                .GroupBy(c => c.name.Contains('_') ? c.name[..c.name.LastIndexOf('_')] : c.name)
                .ToDictionary(g => g.Key, g => g.ToArray());
            oneShot = gameObject.AddComponent<AudioSource>();
            loop = gameObject.AddComponent<AudioSource>();
            loop.loop = true;
            oneShot.playOnAwake = loop.playOnAwake = false;
        }

        // 동작 시작 시 한 번. Animator 상태 진입이나 터치 처리에서 부른다.
        public void Play(string action)
        {
            if (Muted || !isActiveAndEnabled) return;
            if ((action == "greet" || action == "fetch") && sets.ContainsKey(barkSize)) action = barkSize;
            if (!sets.TryGetValue(action, out var clips)) return;
            // 같은 소리가 연달아 나오지 않게 직전 것은 빼고 고른다
            last.TryGetValue(action, out var prev);
            var pool = clips.Length > 1 ? clips.Where(c => c != prev).ToArray() : clips;
            var clip = pool[Random.Range(0, pool.Length)];
            last[action] = clip;
            oneShot.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            oneShot.Stop();
            oneShot.PlayOneShot(clip);
        }

        // 걷기 클립의 발 닿는 프레임에 Animation Event로 연결
        public void Footstep() { if (!oneShot.isPlaying) Play("steps"); }

        // 대기 상태 진입/이탈 시 부른다: 헥헥 반복 + 가끔 생활 소리
        public void StartIdle()
        {
            if (Muted || !isActiveAndEnabled) return;
            if (sets.TryGetValue("idle", out var idle) && !loop.isPlaying)
            {
                loop.clip = idle[0];
                loop.Play();
            }
            life ??= StartCoroutine(LifeLoop());
        }

        public void StopIdle()
        {
            if (loop != null) loop.Stop();
            if (life != null) StopCoroutine(life);
            life = null;
        }

        IEnumerator LifeLoop()
        {
            var available = LifeSets.Where(sets.ContainsKey).ToArray();
            while (available.Length > 0)
            {
                yield return new WaitForSeconds(Random.Range(lifeInterval.x, lifeInterval.y));
                if (!oneShot.isPlaying) Play(available[Random.Range(0, available.Length)]);
            }
        }

        // 즉시 나가기·화면 전환 시 잔류 재생이 없게 (T06)
        void OnDisable()
        {
            StopAll();
        }
        public void StopAll() { StopIdle(); if (oneShot != null) oneShot.Stop(); }
    }
}
