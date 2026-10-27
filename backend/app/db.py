from functools import lru_cache

from psycopg.rows import dict_row
from psycopg_pool import ConnectionPool

from app.config import get_settings


@lru_cache
def get_pool() -> ConnectionPool:
    return ConnectionPool(get_settings().database_url, min_size=1, max_size=5,
                          kwargs={"row_factory": dict_row}, open=True)
