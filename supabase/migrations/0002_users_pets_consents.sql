-- 계정·반려견·동의 (장세찬 #21)
-- 로그인은 Supabase Auth가 맡고, users.id는 토큰의 sub(auth.users.id)와 같다.
-- 로컬 DB에는 auth 스키마가 없어서 auth.users에 외래 키를 걸지 않는다.

create table if not exists users (
    id         uuid primary key,
    created_at timestamptz not null default now()
);

-- 계정당 반려견 1마리 (user_id unique)
create table if not exists pets (
    id             uuid primary key default gen_random_uuid(),
    user_id        uuid not null unique references users (id) on delete cascade,
    name           text not null check (char_length(btrim(name)) between 1 and 30),
    -- 대표 사진. 업로드 API(박재현 #30)가 pet_id를 요구하므로 등록 후 PATCH로 채운다.
    -- media_assets 테이블이 생기면 외래 키를 건다.
    photo_media_id uuid,
    created_at     timestamptz not null default now(),
    updated_at     timestamptz not null default now()
);

-- 동의 기록. 고치지 않고 쌓기만 한다 (언제 어떤 문구에 동의했는지 남긴다).
create table if not exists consents (
    id         uuid primary key default gen_random_uuid(),
    user_id    uuid not null references users (id) on delete cascade,
    pet_id     uuid not null references pets (id) on delete cascade,
    item       text not null check (item in ('gemini_transfer', 'meshy_transfer')),
    version    text not null check (char_length(version) between 1 and 40),
    agreed_at  timestamptz not null default now()
);

create index if not exists consents_pet_item_idx on consents (pet_id, item, agreed_at desc);

-- 앱(anon/authenticated)은 직접 접근하지 않는다. 서버는 서비스 연결로 접근한다.
alter table users enable row level security;
alter table pets enable row level security;
alter table consents enable row level security;
