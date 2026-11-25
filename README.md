# 雅思随身查

离线英汉查词工具，支持中英文查询、词形还原、拼写提示、整段分析和本地生词本。词库含 47,507 个词条，来自 ECDICT，并用 CMUdict 补充部分音标。

## 使用

1. 下载或克隆本仓库，解压后用浏览器打开 `index.html`。
2. 输入英文或中文查释义。选「整段」可粘贴阅读材料，查看词典收录与未收录的词。
3. 在词条页收藏单词，从首页导出 CSV 或 JSON 备份。生词本保存在当前浏览器，可用 JSON 在不同设备之间迁移。

网页也可直接放到静态托管。分享某个词时，可以在页面地址后加 `?q=delve`。点击英音或美音时，浏览器会向有道发音接口发送对应单词。

## Windows 划词

使用 Windows 上的 .NET Framework C# 编译器，在项目根目录运行以下 PowerShell 命令生成热键工具。

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /out:IeltsHotkey.exe .\build\IeltsHotkey.cs
```

双击 `启动划词查词.vbs`。在其他应用选中文字后按 `Ctrl+Alt+D`，即可用 Chrome 或 Edge 打开本地词条小窗。可在 `hotkey.ini` 修改组合键。程序会临时使用系统剪贴板，并尝试恢复原内容。

## 重建和测试

仓库已附带生成后的 `dict-data.js`。更新词库时，从上游下载 [ecdict.csv](https://raw.githubusercontent.com/skywind3000/ECDICT/master/ecdict.csv) 和 [cmudict.dict](https://raw.githubusercontent.com/cmusphinx/cmudict/master/cmudict.dict)，放到本地 `data` 文件夹。需要 Python 3 和 Node.js。

```text
python build/build_dictionary.py --ecdict data/ecdict.csv --cmudict data/cmudict.dict
node build/test.js
```

使用 `python build/pack_single.py` 可以把页面和词库合并为一个 HTML。分享单文件版时，请附上第三方许可说明。

## 许可

项目代码采用 [MIT 许可](LICENSE)。词库中的 ECDICT 和 CMUdict 数据分别沿用其上游许可，见 [第三方许可说明](THIRD_PARTY_NOTICES.txt)。
