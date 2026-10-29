"""API 서버 진입점: `uvicorn app.api.main:app --reload`"""
from fastapi import FastAPI

from app.api.routes import character, health, jobs, pets
from app.errors import install_error_handlers

app = FastAPI(title="곁에 API", version="0.1.0")
install_error_handlers(app)

app.include_router(health.router)
app.include_router(jobs.router)
app.include_router(character.router)
app.include_router(pets.router)
