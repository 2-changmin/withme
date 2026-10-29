"""모든 테이블에 RLS가 켜져 있어야 한다 (docs/04-api-data.md). 새 테이블을 만들면 여기서 걸린다."""
import psycopg
import pytest

from app.config import get_settings

pytestmark = pytest.mark.db


def test_all_public_tables_have_rls():
    with psycopg.connect(get_settings().database_url) as conn:
        rows = conn.execute(
            """
            select c.relname from pg_class c join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = 'public' and c.relkind in ('r', 'p') and not c.relrowsecurity
            order by 1
            """
        ).fetchall()
    assert [r[0] for r in rows] == [], "RLS가 꺼진 테이블"
