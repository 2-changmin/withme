-- 4면 이미지·3D 모델 (장세찬 #23)
-- 각 단계는 작업 큐(jobs)로 돌고, 작업이 끝나면 app/pipeline.py가 결과를 이 테이블로 옮긴다.
-- 파일은 모두 media_assets ID로 가리킨다 (서명 URL은 금방 만료되므로 저장하지 않는다).

create table if not exists reference_views (
    id               uuid primary key default gen_random_uuid(),
    user_id          uuid not null references users (id) on delete cascade,
    pet_id           uuid not null references pets (id) on delete cascade,
    source_media_ids uuid[] not null check (cardinality(source_media_ids) between 1 and 4),
    -- [side, front, back, other]. 작업이 성공하면 채운다
    view_media_ids   uuid[] check (view_media_ids is null or cardinality(view_media_ids) = 4),
    attempt          int not null default 1 check (attempt between 1 and 3),
    -- 재생성이면 이전 시도. 같은 시도를 두 번 재생성 요청해도 한 번만 만든다
    retry_of         uuid unique references reference_views (id) on delete set null,
    job_id           uuid references jobs (id) on delete set null,
    idempotency_key  text,
    approved_at      timestamptz,
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (user_id, idempotency_key)
);

create index if not exists reference_views_pet_idx on reference_views (pet_id, created_at desc);

create table if not exists character_models (
    id                    uuid primary key default gen_random_uuid(),
    user_id               uuid not null references users (id) on delete cascade,
    pet_id                uuid not null references pets (id) on delete cascade,
    -- 4면 1세트로 3D 생성은 한 번만 (승인 중복 요청으로 Meshy가 두 번 청구되지 않게)
    reference_view_id     uuid not null unique references reference_views (id) on delete cascade,
    meshy_task_id         text,
    raw_glb_media_id      uuid references media_assets (id) on delete set null,
    prepared_glb_media_id uuid references media_assets (id) on delete set null,
    joints_auto           jsonb,
    joints_final          jsonb,
    rigged_glb_media_id   uuid references media_assets (id) on delete set null,
    skeleton_version      int not null default 1,
    -- 지금 진행 중이거나 마지막으로 끝난 단계의 작업 (meshy_generate → mesh_prepare → rig)
    job_id                uuid references jobs (id) on delete set null,
    created_at            timestamptz not null default now(),
    updated_at            timestamptz not null default now()
);

create index if not exists character_models_pet_idx on character_models (pet_id, created_at desc);

-- 앱(anon/authenticated)은 직접 접근하지 않는다. 서버는 서비스 연결로 접근한다.
alter table reference_views enable row level security;
alter table character_models enable row level security;
