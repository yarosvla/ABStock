import os

from fastapi import FastAPI
from pydantic import BaseModel
from openai import OpenAI

import json

from transformers import pipeline

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
        return_all_scores=True
    )[0]

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

client = OpenAI(api_key=os.environ.get("OPENAI_API_KEY"))

@app.post("/generate-profile")
def generate_profile(req: dict):
    prompt = req["prompt"]

    response = client.chat.completions.create(
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