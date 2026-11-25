# -*- coding: utf-8 -*-
"""把 dict-data.js 内联进 HTML，生成可以单独拷走的单文件版。"""
import io, os

BUILD_DIR = os.path.dirname(os.path.abspath(__file__))
APP_DIR = os.path.dirname(BUILD_DIR)          # 查词工具/
APP = os.path.join(APP_DIR, "index.html")
DATA = os.path.join(APP_DIR, "dict-data.js")
OUT = os.path.join(APP_DIR, "雅思随身查-单文件版.html")

html = io.open(APP, encoding="utf-8").read()
data = io.open(DATA, encoding="utf-8").read().replace("</script", "<\\/script")

needle = '<script src="dict-data.js"></script>'
assert needle in html, "找不到数据文件的引用"
out = html.replace(needle, "<script>\n" + data.rstrip("\n") + "\n</script>")
io.open(OUT, "w", encoding="utf-8", newline="\n").write(out)
print("单文件版:", round(os.path.getsize(OUT) / 1048576, 2), "MB")
