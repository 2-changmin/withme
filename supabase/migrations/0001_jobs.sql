-- 작업 큐 (박재현 #27)
-- API가 작업을 넣고, 작업자가 claim_job으로 하나씩 가져간다.

create table if not exists jobs (
    id               uuid primary key default gen_random_uuid(),
    type             text not null,
    status           text not null default 'queued'
                     check (status in ('queued', 'running', 'succeeded', 'failed')),
    user_id          uuid not null,
    pet_id           uuid,
    payload          jsonb not null default '{}',
    result           jsonb,
    progress         int not null default 0 check (progress between 0 and 100),
    error_code       text,
    error_message    text,
    attempts         int not null default 0,
    max_attempts     int not null default 3,
    idempotency_key  text,
    locked_by        text,
    lease_expires_at timestamptz,
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    finished_at      timestamptz,
    unique (user_id, idempotency_key)
);

create index if not exists jobs_claimable_idx on jobs (status, created_at)
    where status in ('queued', 'running');

-- 앱(anon/authenticated)은 직접 접근하지 않는다. 서버는 서비스 연결로 접근한다.
alter table jobs enable row level security;

-- 대기 중이거나 lease가 끝난 작업 하나를 가져와 running으로 바꾼다.
-- 여러 작업자가 동시에 불러도 SKIP LOCKED로 같은 작업을 두 번 가져가지 않는다.
create or replace function claim_job(p_worker text, p_types text[], p_lease_seconds int)
returns setof jobs
language sql
as $$
    update jobs set
        status = 'running',
        locked_by = p_worker,
        lease_expires_at = now() + make_interval(secs => p_lease_seconds),
        attempts = attempts + 1,
        updated_at = now()
    where id = (
        select id from jobs
        where type = any(p_types)
          and attempts < max_attempts
          and (status = 'queued' or (status = 'running' and lease_expires_at < now()))
        order by created_at
        for update skip locked
        limit 1
    )
    returning *;
$$;
