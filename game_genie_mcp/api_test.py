import requests

url = 'https://api.chromagolem.com/v1/image/generations'
data = {
    "api_key": "cg-a39529837c83d612dc0e7d0d923c13db4a9c139864a49fb6",
    "client_id": "genie_client",
    "style": "character_portrait",
    "prompt": "A blacksmith specializing in katanas in feudal japan.",
    "negative_prompt": "medieval, europe",
}

headers = {
    "Content-Type": "application/json"
}

response = requests.post(url, json=data, headers=headers)

print(response.json())