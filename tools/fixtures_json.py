"""Builds realistic ComfyUI workflow / prompt JSON for the test fixtures."""
import json

CKPT = "sd_xl_base_1.0.safetensors"
LORA = "detail_tweaker_xl.safetensors"
POSITIVE = "a photo of an astronaut riding a horse on mars, golden hour, 85mm"
NEGATIVE = "blurry, low quality, watermark, text"
POSITIVE_CJK = "赛博朋克风格的城市夜景，霓虹灯，雨后街道，电影感光效"


def prompt_json(positive=POSITIVE, negative=NEGATIVE, seed=873492013):
    """The API format ComfyUI stores under the "prompt" key."""
    return {
        "3": {"class_type": "KSampler", "inputs": {
            "seed": seed, "steps": 24, "cfg": 7.5, "sampler_name": "dpmpp_2m",
            "scheduler": "karras", "denoise": 1.0,
            "model": ["10", 0], "positive": ["6", 0], "negative": ["7", 0],
            "latent_image": ["5", 0]}},
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CKPT}},
        "5": {"class_type": "EmptyLatentImage",
              "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": positive, "clip": ["10", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["10", 1]}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": "ComfyUI", "images": ["8", 0]}},
        "10": {"class_type": "LoraLoader", "inputs": {
            "lora_name": LORA, "strength_model": 0.75, "strength_clip": 0.75,
            "model": ["4", 0], "clip": ["4", 1]}},
    }


def _node(node_id, node_type, order, widgets, pos=(100, 100)):
    return {
        "id": node_id, "type": node_type, "pos": list(pos), "size": [315, 262],
        "flags": {}, "order": order, "mode": 0,
        "inputs": [], "outputs": [], "properties": {"Node name for S&R": node_type},
        "widgets_values": widgets,
    }


def graph_json(positive=POSITIVE, negative=NEGATIVE, seed=873492013):
    """The editor graph ComfyUI stores under the "workflow" key."""
    return {
        "last_node_id": 10,
        "last_link_id": 14,
        "nodes": [
            _node(4, "CheckpointLoaderSimple", 0, [CKPT], (26, 474)),
            _node(10, "LoraLoader", 1, [LORA, 0.75, 0.75], (330, 474)),
            _node(5, "EmptyLatentImage", 2, [1024, 1024, 1], (473, 609)),
            _node(6, "CLIPTextEncode", 3, [positive], (415, 186)),
            _node(7, "CLIPTextEncode", 4, [negative], (413, 389)),
            _node(3, "KSampler", 5, [seed, "randomize", 24, 7.5, "dpmpp_2m", "karras", 1],
                  (863, 186)),
            _node(8, "VAEDecode", 6, [], (1209, 188)),
            _node(9, "SaveImage", 7, ["ComfyUI"], (1451, 189)),
        ],
        "links": [
            [1, 4, 0, 10, 0, "MODEL"], [2, 4, 1, 10, 1, "CLIP"],
            [3, 10, 0, 3, 0, "MODEL"], [4, 10, 1, 6, 0, "CLIP"],
            [5, 10, 1, 7, 0, "CLIP"], [6, 6, 0, 3, 1, "CONDITIONING"],
            [7, 7, 0, 3, 2, "CONDITIONING"], [8, 5, 0, 3, 3, "LATENT"],
            [9, 3, 0, 8, 0, "LATENT"], [10, 4, 2, 8, 1, "VAE"],
            [11, 8, 0, 9, 0, "IMAGE"],
        ],
        "groups": [], "config": {}, "extra": {"ds": {"scale": 0.9, "offset": [0, 0]}},
        "version": 0.4,
    }


def dumps(value, ascii_only=True):
    return json.dumps(value, ensure_ascii=ascii_only, separators=(",", ":"))


def wrapper_json(ascii_only=True):
    """What VideoHelperSuite puts in a single video comment tag."""
    return dumps({"prompt": prompt_json(), "workflow": graph_json()}, ascii_only)
