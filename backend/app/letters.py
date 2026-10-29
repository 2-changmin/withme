"""무지개다리 편지 저장소 (supabase/migrations/0005_letters.sql, #74).

보호자가 편지를 쓰면 letter_reply 작업이 반려견 말투로 답장을 쓴다. 편지 1통에 답장 1통.
안전장치 (docs/decisions/2026-10-30-rainbow-letter.md)
- 위기 표현이 보이면 답장하지 않고 상담 안내를 보여 준다 (여기 키워드 + 작업자의 LLM 판단)
- 하루 1통 (반려견별, 한국 시간 기준). 위기·실패한 편지는 세지 않는다
"""
from uuid import UUID

from app import queue
from app.db import get_pool
from app.errors import AppError, ErrorCode
from app.schemas import JobType, Letter

DAILY_LIMIT = 1

# 공백을 지운 편지에서 찾는다 ("죽고 싶" → "죽고싶")
CRISIS_KEYWORDS = (
    "죽고싶", "죽을래", "자살", "자해", "살기싫", "살고싶지않", "사라지고싶", "끝내고싶",
    "따라가고싶", "따라갈게", "너한테갈게", "곁으로갈게", "같이가고싶", "목숨", "극단적",
)

CRISIS_HELP = (
    "마음이 많이 힘드시군요. 혼자 견디지 않으셔도 돼요.\n"
    "자살예방상담전화 109 (24시간), 정신건강위기상담 1577-0199에 연락하면 "
    "이야기를 들어 줄 사람이 있어요.\n"
    "지금 위험한 상황이라면 112나 119에 바로 연락해 주세요."
)


def has_crisis(text: str) -> bool:
    compact = "".join(text.split())
    return any(k in compact for k in CRISIS_KEYWORDS)


def notice(pet_name: str) -> str:
    return f"AI가 {pet_name}의 모습을 떠올려 쓴 답장이에요."


# 작업이 실패한 pending 편지는 failed로 보여 준다
_SELECT = """
    select l.id, l.pet_id, l.body, l.reply, l.job_id, l.created_at, l.replied_at, p.name as pet_name,
           case when l.status = 'pending' and j.status = 'failed' then 'failed'
                else l.status end as status
    from letters l
    join pets p on p.id = l.pet_id
    left join jobs j on j.id = l.job_id
"""


def to_model(row: dict) -> Letter:
    return Letter.model_validate({
        **row,
        "help": CRISIS_HELP if row["status"] == "crisis" else None,
        "notice": notice(row["pet_name"]),
    })


def create(user_id: UUID, pet_id: UUID, body: str) -> dict:
    crisis = has_crisis(body)
    with get_pool().connection() as conn:
        # 같은 반려견의 동시 요청이 하루 제한을 함께 통과하지 않게 잠근다
        conn.execute("select 1 from pets where id = %s for update", (pet_id,))
        if not crisis:
            sent = conn.execute(
                """
                select count(*) as n from letters l left join jobs j on j.id = l.job_id
                where l.pet_id = %s and l.status <> 'crisis'
                  and not (l.status = 'pending' and j.status = 'failed')
                  and (l.created_at at time zone 'Asia/Seoul')::date
                      = (now() at time zone 'Asia/Seoul')::date
                """,
                (pet_id,),
            ).fetchone()["n"]
            if sent >= DAILY_LIMIT:
                raise AppError(ErrorCode.LIMIT_EXCEEDED, f"편지는 하루 {DAILY_LIMIT}통까지 쓸 수 있습니다")
        row = conn.execute(
            "insert into letters (user_id, pet_id, body, status) values (%s, %s, %s, %s) returning id",
            (user_id, pet_id, body, "crisis" if crisis else "pending"),
        ).fetchone()
        if not crisis:
            job_id = queue.enqueue(JobType.LETTER_REPLY, user_id, {"letterId": str(row["id"])},
                                   pet_id=pet_id, idempotency_key=f"letter_reply:{row['id']}",
                                   conn=conn)
            conn.execute("update letters set job_id = %s where id = %s", (job_id, row["id"]))
    return get(user_id, row["id"])


def list_for_pet(pet_id: UUID) -> list[dict]:
    with get_pool().connection() as conn:
        return conn.execute(_SELECT + " where l.pet_id = %s order by l.created_at desc",
                            (pet_id,)).fetchall()


def get(user_id: UUID, letter_id: UUID | str) -> dict:
    """남의 편지든 없는 편지든 같은 404."""
    try:
        letter_id = UUID(str(letter_id))
    except ValueError as e:
        raise AppError(ErrorCode.NOT_FOUND, "편지가 없습니다") from e
    with get_pool().connection() as conn:
        row = conn.execute(_SELECT + " where l.id = %s and l.user_id = %s",
                           (letter_id, user_id)).fetchone()
    if row is None:
        raise AppError(ErrorCode.NOT_FOUND, "편지가 없습니다")
    return row


def delete(user_id: UUID, letter_id: UUID | str) -> None:
    get(user_id, letter_id)
    with get_pool().connection() as conn:
        conn.execute("delete from letters where id = %s and user_id = %s", (letter_id, user_id))


def load_for_reply(letter_id: str) -> dict:
    """작업자용: 답장에 필요한 편지 본문과 반려견 이름."""
    with get_pool().connection() as conn:
        row = conn.execute(
            "select l.id, l.body, p.name as pet_name from letters l join pets p on p.id = l.pet_id"
            " where l.id = %s",
            (letter_id,),
        ).fetchone()
    if row is None:
        raise AppError(ErrorCode.VALIDATION_FAILED, f"편지가 없습니다: {letter_id}")
    return row
