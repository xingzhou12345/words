"""Build dict-data.js from ECDICT and CMUdict."""
import argparse
import csv
import io
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--ecdict", type=Path, required=True, help="Path to upstream ecdict.csv")
parser.add_argument("--cmudict", type=Path, required=True, help="Path to upstream cmudict.dict")
parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "dict-data.js")
args = parser.parse_args()
for data_file in (args.ecdict, args.cmudict):
    if not data_file.is_file():
        parser.error(f"Data file not found: {data_file}")

TAGS = {"ielts": "雅思", "toefl": "托福", "gre": "GRE", "cet6": "六级",
        "cet4": "四级", "ky": "考研", "gk": "高考", "zk": "中考"}
TAG_ORDER = ["ielts", "toefl", "gre", "cet6", "cet4", "ky", "gk", "zk"]
BNC_LIM, FRQ_LIM = 40000, 40000

def tagset(d):
    return [t for t in (d.get("tag") or "").split() if t in TAGS]


def norm_phon(s):
    """ECDICT 用 . 和 ' 表示次重音/主重音，转成 IPA 符号更顺眼。"""
    s = (s or "").strip().strip("[]/")
    s = s.replace("ә", "ə")
    s = s.replace(":", "ː")
    s = re.sub(r"^\.", "ˌ", s)
    s = s.replace("'", "ˈ")
    return s.strip()


def norm_pos(s):
    """ECDICT 的 pos 是 'n:46/v:54' 这种词性占比，显示时简化成 'n./v.'。"""
    s = (s or "").strip()
    if not s or ":" not in s:
        return ""
    parts = []
    for seg in s.split("/"):
        k = seg.split(":")[0].strip()
        if k and k not in parts:
            parts.append(k)
    return "/".join(p + "." for p in parts) if parts else ""


def norm_zh(s):
    """换行改成中文分号，英文逗号在中文之间改成全角，出来更像词典。"""
    s = (s or "").strip()
    s = s.replace("\\n", "；")
    s = re.sub(r"\r?\n+", "；", s)
    s = re.sub(r"；+", "；", s)
    s = re.sub(r"^[，,；;、\s]+", "", s)
    s = re.sub(r"[，,；;、\s]+$", "", s)
    s = re.sub(r"(?<=[\u4e00-\u9fff]),(?=[\u4e00-\u9fff])", "，", s)
    s = re.sub(r"(?<=[\u4e00-\u9fff]), (?=[\u4e00-\u9fff])", "，", s)
    # 「…方法adv. 无论如何」这种粘连，补一个分号
    s = re.sub(r"(?<=[\u4e00-\u9fff])(?=(?:n|vt|vi|v|adj|adv|ad|a|prep|conj|pron|num|art|int|aux)\.\s)", "；", s)
    return s.strip()


def as_int(v):
    try:
        return int(float(v or 0))
    except ValueError:
        return 0

records = []
# 从全量 CSV 收集"变形词 → 原形"（0: 是 ECDICT 标注的 lemma），
# 覆盖那些自身没进子集的变形词（were → be、lives → life …）
lemma_map = {}

with io.open(args.ecdict, encoding="utf-8", errors="replace", newline="") as f:
    r = csv.DictReader(f)
    for d in r:
        w = (d["word"] or "").strip()
        if not w:
            continue
        lw = w.lower()
        exch = (d["exchange"] or "").strip()
        if "0:" in exch:
            for part in exch.split("/"):
                if part.startswith("0:"):
                    lem = part[2:].strip().lower()
                    if lem and lem != lw:
                        lst = lemma_map.setdefault(lw, [])
                        if lem not in lst:
                            lst.append(lem)
        zh = (d["translation"] or "").strip()
        if not zh:
            continue
        col = as_int(d["collins"])
        ox = d["oxford"] == "1"
        tags = tagset(d)
        bnc, frq = as_int(d["bnc"]), as_int(d["frq"])
        if not (ox or col or tags or (0 < min(bnc or 10**9, frq or 10**9) <= min(BNC_LIM, FRQ_LIM))):
            continue
        # 人名条目对雅思没有价值，删掉（地名保留，阅读里会碰到）
        if "人名" in zh and not (tags or ox or col):
            continue
        rank = min([x for x in (bnc, frq) if x > 0] or [999999])
        rec = {
            "w": w, "ph": norm_phon(d["phonetic"]), "pos": norm_pos(d["pos"]),
            "zh": norm_zh(zh), "en": (d["definition"] or "").strip()[:300],
            "col": col, "ox": ox, "tags": tags, "rank": rank,
            "exch": (d["exchange"] or "").strip(),
        }
        records.append(rec)

print("ecdict subset:", len(records))
# index records by word
by_word = {r["w"].lower(): r for r in records}

def fmt_forms(exch):
    names = {"p": "过去式", "d": "过去分词", "i": "现在分词", "3": "三单",
             "r": "比较级", "t": "最高级", "s": "复数"}
    out = []
    for part in (exch or "").split("/"):
        if ":" not in part:
            continue
        k, v = part.split(":", 1)
        if k in names and v:
            out.append(f"{names[k]} {v}")
    return "；".join(out)

ARPA_VOWELS = {"AA", "AE", "AH", "AO", "AW", "AY", "EH", "ER", "EY", "IH", "IY",
               "OW", "OY", "UH", "UW"}
ARPA_IPA = {
    "AA": "ɑː", "AE": "æ", "AH": "ʌ", "AO": "ɔː", "AW": "aʊ", "AY": "aɪ",
    "B": "b", "CH": "tʃ", "D": "d", "DH": "ð", "EH": "e", "ER": "ɜːr",
    "EY": "eɪ", "F": "f", "G": "ɡ", "HH": "h", "IH": "ɪ", "IY": "iː",
    "JH": "dʒ", "K": "k", "L": "l", "M": "m", "N": "n", "NG": "ŋ",
    "OW": "əʊ", "OY": "ɔɪ", "P": "p", "R": "r", "S": "s", "SH": "ʃ",
    "T": "t", "TH": "θ", "UH": "ʊ", "UW": "uː", "V": "v", "W": "w",
    "Y": "j", "Z": "z", "ZH": "ʒ",
}


def arpa_to_ipa(tokens):
    items = []
    for tok in tokens:
        m = re.match(r"^([A-Z]+)([0-2]?)$", tok)
        if not m:
            return ""
        base, st = m.group(1), m.group(2)
        if base not in ARPA_IPA:
            return ""
        ipa = ARPA_IPA[base]
        if base == "AH" and st == "0":
            ipa = "ə"
        if base == "ER" and st == "0":
            ipa = "ər"
        items.append([ipa, base in ARPA_VOWELS, int(st) if st else 0])
    # 重音符号放到音节开头（往前找最多 3 个辅音）
    for i, (ipa, is_v, st) in enumerate(items):
        if is_v and st in (1, 2):
            j = i
            while j - 1 >= 0 and not items[j - 1][1] and (i - j) < 3:
                j -= 1
            items[j][0] = ("ˈ" if st == 1 else "ˌ") + items[j][0]
    return "".join(x[0] for x in items)


def load_cmudict(path):
    table = {}
    with io.open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith(";;;"):
                continue
            sp = line.find(" ")
            if sp <= 0:
                continue
            w = line[:sp].lower()
            if w.endswith(")") and "(" in w:
                w = w[:w.rindex("(")]
            if w in table:
                continue
            ipa = arpa_to_ipa(line[sp + 1:].split())
            if ipa:
                table[w] = ipa
    return table


cmu = load_cmudict(args.cmudict)
for record in records:
    if not record["ph"]:
        record["ph"] = cmu.get(record["w"].lower(), "")

# ---------- inflection aliases ---------------------------------------------
# alias: 变形词 → [原形...]。一个变形可能对应多个原形（lives → life / live）
aliases = {}

def add_alias(form, base):
    form, base = form.lower(), base.lower()
    if not form or form == base or form in by_word:
        return
    if base not in by_word:      # 原形没进发布词库，这条映射查不到东西
        return
    lst = aliases.setdefault(form, [])
    if base not in lst:
        lst.append(base)

for rec in records:
    for part in (rec["exch"] or "").split("/"):
        if ":" not in part:
            continue
        k, v = part.split(":", 1)
        if k in "pdi3rts" and v:
            add_alias(v, rec["w"])

# ECDICT 的 0: lemma 标注（能补上自身没进子集的变形词）
for form, lemmas in lemma_map.items():
    for lem in lemmas:
        base_rec = by_word.get(lem)
        if base_rec:
            add_alias(form, base_rec["w"])

# 少数不规则变形 ECDICT 没标 lemma，手工兜底（只补字典里没有的）
IRREGULAR = {
    "was": "be", "were": "be", "been": "be", "being": "be",
    "am": "be", "is": "be", "are": "be",
    "has": "have", "had": "have", "having": "have",
    "does": "do", "did": "do", "done": "do", "doing": "do",
    "went": "go", "gone": "go", "goes": "go",
    "made": "make", "took": "take", "taken": "take",
    "came": "come", "saw": "see", "seen": "see",
    "got": "get", "gotten": "get", "knew": "know", "known": "know",
    "thought": "think", "said": "say", "gave": "give", "given": "give",
    "found": "find", "told": "tell", "became": "become", "left": "leave",
    "felt": "feel", "brought": "bring", "began": "begin", "begun": "begin",
    "kept": "keep", "held": "hold", "wrote": "write", "written": "write",
    "stood": "stand", "heard": "hear", "meant": "mean", "met": "meet",
    "ran": "run", "paid": "pay", "sat": "sit", "spoke": "speak",
    "spoken": "speak", "led": "lead", "grew": "grow", "grown": "grow",
    "lost": "lose", "fell": "fall", "fallen": "fall", "sent": "send",
    "built": "build", "spent": "spend", "cut": "cut", "put": "put",
}
for form, lem in IRREGULAR.items():
    if form not in by_word:
        add_alias(form, lem)

print("aliases:", len(aliases), "| 多候选:", sum(1 for v in aliases.values() if len(v) > 1))

def en_for(rec):
    """English gloss only where it earns its bytes: core/exam words."""
    if not (rec["col"] or rec["ox"] or rec["tags"]):
        return ""
    s = " ".join(rec["en"].split())
    return s[:150]

# Keep the reserved textbook columns empty so the browser uses the same row schema.
rows = [
    [
        record["w"], record["ph"], record["pos"], record["zh"], en_for(record),
        record["col"], int(record["ox"]),
        " ".join(TAGS[tag] for tag in TAG_ORDER if tag in record["tags"]),
        record["rank"], fmt_forms(record["exch"]),
        0, 0, 0, "", "", "", "", "", "", 0,
    ]
    for record in records
]
rows.sort(key=lambda row: (row[8], row[0].lower()))
payload = {
    "v": 1,
    "src": "ECDICT (MIT) + CMUdict; see THIRD_PARTY_NOTICES.txt",
    "n": len(rows),
    "rows": rows,
    "alias": aliases,
}
with args.output.open("w", encoding="utf-8", newline="\n") as output:
    output.write("/* Generated dictionary. See THIRD_PARTY_NOTICES.txt. */\nwindow.__DICT__=")
    json.dump(payload, output, ensure_ascii=False, separators=(",", ":"))
    output.write(";\n")

print(f"{len(rows):,} entries, {len(aliases):,} inflections. Wrote {args.output.name}.")
