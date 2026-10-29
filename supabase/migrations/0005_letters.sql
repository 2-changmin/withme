-- 무지개다리 편지 (박재현 #74, docs/decisions/2026-10-30-rainbow-letter.md)
-- 보호자가 쓴 편지와 AI가 반려견 말투로 쓴 답장. 편지 1통에 답장 1통이고 이어지는 대화는 없다.

create table if not exists letters (
    id          uuid primary key default gen_random_uuid(),
    user_id     uuid not null references users (id) on delete cascade,
    pet_id      uuid not null references pets (id) on delete cascade,
    body        text not null check (char_length(body) between 1 and 2000),
    reply       text,
    -- pending: 답장 쓰는 중 / replied: 답장 도착 / crisis: 위기 표현이라 답장 대신 상담 안내
    status      text not null default 'pending'
                check (status in ('pending', 'replied', 'crisis')),
    job_id      uuid references jobs (id) on delete set null,
    created_at  timestamptz not null default now(),
    replied_at  timestamptz,
    updated_at  timestamptz not null default now()
);

create index if not exists letters_pet_idx on letters (pet_id, created_at desc);

-- 앱(anon/authenticated)은 직접 접근하지 않는다. 서버는 서비스 연결로 접근한다.
alter table letters enable row level security;
