# 기준점 JSON (임시 형식 `dog-landmarks-tmp/0`)

#17의 관절 JSON 형식이 확정되기 전까지 쓰는 임시 형식이다. 확정되면 `blender/dogrig/landmarks.py`의 `load`만 바꾸면 된다.

```json
{
  "format": "dog-landmarks-tmp/0",
  "space": "blender: front -Y, up +Z, left +X, height normalized to 1, feet at z=0",
  "source": "estimated | manual | file",
  "points": {
    "nose":             [x, y, z],
    "neck":             [x, y, z],
    "tail_start":       [x, y, z],
    "tail_end":         [x, y, z],
    "front_shoulder_l": [x, y, z],
    "front_paw_l":      [x, y, z],
    "hind_hip_l":       [x, y, z],
    "hind_paw_l":       [x, y, z],
    "ear_root_l":       [x, y, z],
    "ear_tip_l":        [x, y, z]
  },
  "hints": {
    "tail_curve": [[x, y, z], ...5개],
    "spine_z": 0.55
  },
  "meta": { "model": "m2_white.glb" }
}
```

## 점의 의미

| 점 | 위치 |
|---|---|
| `nose` | 코 끝 |
| `neck` | 목과 머리가 만나는 곳 (뒤통수 아래 중심) |
| `tail_start` / `tail_end` | 꼬리 뿌리 / 꼬리 끝 |
| `front_shoulder_l` | 왼쪽 앞다리가 몸통에 붙는 높이 (척추 높이) |
| `front_paw_l` | 왼쪽 앞발 기둥 중심, 바닥(z=0) |
| `hind_hip_l` | 왼쪽 골반(고관절) |
| `hind_paw_l` | 왼쪽 뒷발 기둥 중심, 바닥(z=0) |
| `ear_root_l` / `ear_tip_l` | 왼쪽 귀 뿌리 / 끝 |

- 좌표는 정규화 공간 기준이다(실제 cm 아님).
- 오른쪽은 x를 반전해 만든다. 비대칭 모델은 오른쪽 오차가 생긴다.
- `hints`는 선택 사항이다. `tail_curve`가 없으면 꼬리는 시작~끝 직선으로 나눈다.

## 수동 수정 흐름

1. `rig.py ... --blend out.blend` 또는 `estimate_landmarks.py ... --blend out.blend`로 `LM_*` 빈 객체가 들어간 .blend를 만든다.
2. Blender에서 `LM_*`를 옮긴다.
3. Text Editor에서 `blender/landmarks_from_blend.py`를 실행한다. .blend 옆에 `landmarks.json`이 생긴다.
4. `rig.py ... --landmarks landmarks.json`으로 다시 리깅한다.
