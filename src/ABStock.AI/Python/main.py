import os

from pathlib import Path

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from openai import OpenAI

import json

from transformers import pipeline

from dotenv import load_dotenv

ROOT_DIR = Path(__file__).resolve().parents[3]
load_dotenv(ROOT_DIR / ".env")

app = FastAPI()

classifier = pipeline(
    "text-classification",
    model="ProsusAI/finbert"
)

class Request(BaseModel):
    text: str

@app.post("/analyze")
def analyze(req: Request):

    results = classifier(
        req.text,
        top_k=None
    )

    positive = 0.0
    neutral = 0.0
    negative = 0.0

    for item in results:

        label = item["label"].lower()
        score = item["score"]

        if label == "positive":
            positive = score

        elif label == "neutral":
            neutral = score

        elif label == "negative":
            negative = score

    return {
        "positive": positive,
        "neutral": neutral,
        "negative": negative
    }

# Клиент создаётся при первом запросе: OpenAI() без ключа бросает исключение,
# и на уровне модуля это роняло весь сервис — вместе с FinBERT, которому ключ
# не нужен.
_client = None

def get_client():
    global _client
    if _client is None:
        api_key = os.environ.get("OPENAI_API_KEY")
        if not api_key:
            raise HTTPException(status_code=503, detail="OPENAI_API_KEY is not set")
        _client = OpenAI(api_key=api_key)
    return _client

@app.post("/generate-profile")
def generate_profile(req: dict):
    prompt = req["prompt"]

    response = get_client().chat.completions.create(
        model="gpt-4o-mini",
        messages=[
            {
                "role": "system",
                "content": "You are a financial analyst. Return ONLY valid JSON. No text."
            },
            {
                "role": "user",
                "content": prompt
            }
        ],
        response_format={
            "type": "json_object"
        }
    )

    return json.loads(response.choices[0].message.content)