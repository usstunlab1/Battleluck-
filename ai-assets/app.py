from flask import Flask, jsonify, request
import requests
import os
import logging

app = Flask(__name__)
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

LLM_HOST = os.getenv('LLM_HOST', 'http://llm:11434')
GOOGLE_HOST = os.getenv('GOOGLE_HOST', 'http://google:8000')
REQUEST_TIMEOUT = 5


@app.route('/health', methods=['GET'])
def health():
    return jsonify({"status": "ok"}), 200


@app.route('/status', methods=['GET'])
def status():
    llm_status = "down"
    google_status = "down"
    
    try:
        r = requests.get(f"{LLM_HOST}/api/tags", timeout=REQUEST_TIMEOUT)
        llm_status = "up" if r.status_code == 200 else "down"
    except requests.RequestException as e:
        logger.warning(f"LLM health check failed: {e}")
    
    try:
        r = requests.get(f"{GOOGLE_HOST}/status", timeout=REQUEST_TIMEOUT)
        google_status = "up" if r.status_code == 200 else "down"
    except requests.RequestException as e:
        logger.warning(f"Google health check failed: {e}")
    
    return jsonify({
        "service": "ai-assets",
        "llm": llm_status,
        "google": google_status,
        "llm_url": LLM_HOST,
        "google_url": GOOGLE_HOST
    }), 200


@app.route('/llm/models', methods=['GET'])
def llm_models():
    try:
        r = requests.get(f"{LLM_HOST}/api/tags", timeout=REQUEST_TIMEOUT)
        if r.status_code == 200:
            return jsonify(r.json()), 200
        return jsonify({"error": "LLM service unavailable"}), 503
    except requests.RequestException as e:
        logger.error(f"LLM models request failed: {e}")
        return jsonify({"error": str(e)}), 503


@app.route('/llm/generate', methods=['POST'])
def llm_generate():
    try:
        data = request.get_json()
        if not data or 'prompt' not in data:
            return jsonify({"error": "missing prompt"}), 400
        
        payload = {
            "model": data.get('model', 'llama2'),
            "prompt": data['prompt'],
            "stream": False
        }
        
        r = requests.post(f"{LLM_HOST}/api/generate", json=payload, timeout=30)
        if r.status_code == 200:
            return jsonify(r.json()), 200
        logger.error(f"LLM generation failed with status {r.status_code}")
        return jsonify({"error": "LLM generation failed"}), 500
    except requests.RequestException as e:
        logger.error(f"LLM generation request failed: {e}")
        return jsonify({"error": str(e)}), 503
    except Exception as e:
        logger.error(f"Unexpected error in llm_generate: {e}")
        return jsonify({"error": "Internal server error"}), 500


@app.route('/google/generate', methods=['POST'])
def google_generate():
    try:
        data = request.get_json()
        if not data or 'prompt' not in data:
            return jsonify({"error": "missing prompt"}), 400
        
        r = requests.post(f"{GOOGLE_HOST}/generate", json=data, timeout=30)
        return jsonify(r.json()), r.status_code
    except requests.RequestException as e:
        logger.error(f"Google generation request failed: {e}")
        return jsonify({"error": str(e)}), 503
    except Exception as e:
        logger.error(f"Unexpected error in google_generate: {e}")
        return jsonify({"error": "Internal server error"}), 500


if __name__ == '__main__':
    logger.info(f"Starting AI Assets on port 5000 (LLM: {LLM_HOST}, Google: {GOOGLE_HOST})")
    app.run(host='0.0.0.0', port=5000, debug=False)
