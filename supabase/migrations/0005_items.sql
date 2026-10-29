-- 나의 방 물건: 장난감·간식·애착자리 (윤승희 #76)
-- 3종 모두 누끼(item_cutout) → 4면(item_views) → 3D(meshy_generate) 순서로 만든다.
-- 장난감은 누끼까지만 만들어 두고, 가챠로 뽑힌 것만 4면·3D로 만든다.
-- 간식·애착자리는 누끼가 끝나면 바로 4면·3D로 넘어간다 (app/pipeline.py).

create table if not exists items (
    id                 uuid primary key default gen_random_uuid(),
    user_id            uuid not null references users (id) on delete cascade,
    pet_id             uuid not null references pets (id) on delete cascade,
    kind               text not null check (kind in ('toy', 'snack', 'bed')),
    source_media_id    uuid not null references media_assets (id) on delete cascade,
    -- 투명 배경 PNG
    cutout_media_id    uuid references media_assets (id) on delete set null,
    -- 4면·3D 생성을 시작한 시각. 장난감은 가챠에 뽑힌 시각이고, 가챠 후보는 이 값이 비어 있는 것만
    model_requested_at timestamptz,
    -- [side, front, back, other]
    view_media_ids     uuid[] check (view_media_ids is null or cardinality(view_media_ids) = 4),
    glb_media_id       uuid references media_assets (id) on delete set null,
    -- 지금 진행 중이거나 마지막으로 끝난 단계의 작업 (item_cutout → item_views → meshy_generate)
    job_id             uuid references jobs (id) on delete set null,
    idempotency_key    text,
    created_at         timestamptz not null default now(),
    updated_at         timestamptz not null default now(),
    unique (user_id, idempotency_key)
);

create index if not exists items_pet_idx on items (pet_id, kind, created_at desc);

-- 앱(anon/authenticated)은 직접 접근하지 않는다. 서버는 서비스 연결로 접근한다.
alter table items enable row level security;
