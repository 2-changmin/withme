-- 미디어 파일 목록 (박재현 #30)
-- 파일 자체는 비공개 버킷(로컬은 LOCAL_MEDIA_DIR)에 두고, 여기에는 경로와 상태만 둔다.
-- 앱에는 짧은 만료 서명 URL로만 내려준다.

create table if not exists media_assets (
    id          uuid primary key default gen_random_uuid(),
    user_id     uuid not null references users (id) on delete cascade,
    pet_id      uuid not null references pets (id) on delete cascade,
    -- upload: 보호자가 올린 원본 / generated_visual: 4면 이미지 / generated_model: GLB
    -- thumbnail: media_check가 만든 썸네일
    source_type text not null
                check (source_type in ('upload', 'generated_visual', 'generated_model', 'thumbnail')),
    -- 무엇에서 만들어졌는지 (4면 이미지 → 원본 사진, 썸네일 → 원본)
    parent_id   uuid references media_assets (id) on delete set null,
    path        text not null unique,
    mime_type   text not null,
    size_bytes  bigint not null check (size_bytes >= 0),
    -- pending: 업로드 URL만 발급 / uploaded: 업로드 완료, 검사 대기 / ready: 사용 가능 / rejected: 거절
    status      text not null default 'pending'
                check (status in ('pending', 'uploaded', 'ready', 'rejected')),
    error_code  text,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now()
);

create index if not exists media_assets_pet_idx on media_assets (pet_id, source_type, status);

-- 앱(anon/authenticated)은 직접 접근하지 않는다. 서버는 서비스 연결로 접근한다.
alter table media_assets enable row level security;

-- 0002에서 미뤄 둔 대표 사진 외래 키
do $$ begin
    alter table pets add constraint pets_photo_media_fk
        foreign key (photo_media_id) references media_assets (id) on delete set null;
exception when duplicate_object then null;
end $$;

-- Supabase에서만: 비공개 버킷. 로컬 PostgreSQL에는 storage 스키마가 없어서 건너뛴다.
-- 용량 상한은 app/media.py의 가장 큰 상한(영상 50MB)과 맞춘다. Free 플랜은 파일당 50MB가 최대다.
do $$ begin
    if exists (select 1 from information_schema.schemata where schema_name = 'storage') then
        insert into storage.buckets (id, name, public, file_size_limit)
        values ('media', 'media', false, 52428800)
        on conflict (id) do nothing;
    end if;
end $$;
