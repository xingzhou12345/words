// Headless logic test for index.html (no browser available in this sandbox).
const fs = require('fs');
const vm = require('vm');
const path = require('path');

const DIR = path.join(__dirname, '..');
let html = fs.readFileSync(path.join(DIR, 'index.html'), 'utf8');
let data = fs.readFileSync(path.join(DIR, 'dict-data.js'), 'utf8');
let appScript = (html.match(/<script>\n([\s\S]*?)\n<\/script>/) || [])[1];
if (process.argv[2]) {
  // 单文件版：第一个内联 script 是数据，最后一个才是应用
  html = fs.readFileSync(process.argv[2], 'utf8');
  const blocks = [...html.matchAll(/<script>\n([\s\S]*?)\n<\/script>/g)].map(m => m[1]);
  data = blocks[0];
  appScript = blocks[blocks.length - 1];
  console.log('testing single-file build:', path.basename(process.argv[2]), '| blocks =', blocks.length);
}
if (!appScript) { console.error('app script not found'); process.exit(1); }

function boot(href, sharedStorage) {
  const elements = {};
  function mkEl(id) {
    const el = {
      id, value: '', textContent: '', innerHTML: '', style: {}, dataset: {}, scrollTop: 0, _h: {},
      classList: {
        _s: new Set(),
        add(c) { this._s.add(c); }, remove(c) { this._s.delete(c); },
        toggle(c, f) { const on = f === undefined ? !this._s.has(c) : !!f; on ? this._s.add(c) : this._s.delete(c); return on; },
        contains(c) { return this._s.has(c); },
      },
      addEventListener(t, f) { (this._h[t] = this._h[t] || []).push(f); },
      removeEventListener() {}, focus() {}, blur() {}, select() {}, scrollIntoView() {}, click() {}, remove() {},
      appendChild() {}, querySelector() { return null; }, querySelectorAll() { return []; }, closest() { return null; },
      fire(t, ev) { (this._h[t] || []).forEach(f => f(Object.assign({ preventDefault() {}, target: el }, ev || {}))); },
    };
    return el;
  }
  const byId = (id) => elements[id] || (elements[id] = mkEl(id));
  const mkClassList = () => ({
    _s: new Set(),
    add(c) { this._s.add(c); }, remove(c) { this._s.delete(c); },
    toggle(c, f) { const on = f === undefined ? !this._s.has(c) : !!f; on ? this._s.add(c) : this._s.delete(c); return on; },
    contains(c) { return this._s.has(c); },
  });
  const storage = sharedStorage || {};
  const played = [];
  const downloads = [];
  const sandbox = {
    console, setTimeout, clearTimeout,
    document: {
      getElementById: byId, createElement(tag) {
        const el = mkEl('dyn');
        if (tag === 'input') el.click = () => {
          el.files = [{contents: sandbox.importContents}];
          el.onchange();
        };
        return el;
      },
      addEventListener() {}, querySelector: () => mkEl('main'),
      body: { classList: mkClassList(), appendChild() {} }, activeElement: null,
    },
    localStorage: {
      get length() { return Object.keys(storage).length; },
      key(i) { return Object.keys(storage)[i] || null; },
      getItem(k) { return k in storage ? storage[k] : null; },
      setItem(k, v) {
        if (storage.__failWrites) throw new Error('quota');
        if (storage.__beforeWrite) storage.__beforeWrite(k, v);
        storage[k] = String(v);
      },
    },
    navigator: { clipboard: { writeText: () => Promise.resolve() } },
    Audio: class { constructor(u) { this.src = u; played.push(u); } play() { return Promise.resolve(); } pause() {} },
    speechSynthesis: { speak() {}, cancel() {} },
    SpeechSynthesisUtterance: class { constructor(t) { this.text = t; } },
    Blob: class { constructor(parts, options) { this.parts = parts; this.type = options.type; } },
    FileReader: class { readAsText(file) { this.result = file.contents; this.onload(); } },
    URL: { createObjectURL(blob) { downloads.push(blob); return 'blob:test'; }, revokeObjectURL() {} },
    location: { href: href || 'file:///test.html' },
    addEventListener(t, f) { (sandbox._events[t] = sandbox._events[t] || []).push(f); },
    _events: {},
  };
  sandbox.window = sandbox;
  const ctx = vm.createContext(sandbox);
  vm.runInContext(data, ctx, { filename: 'dict-data.js' });
  vm.runInContext(appScript, ctx, { filename: 'app.js' });
  return { byId, storage, window: sandbox, played, downloads,
    fireStorage(key) { (sandbox._events.storage || []).forEach(f => f({key})); },
  };
}

function savedFavorites(storage) {
  return Array.from(boot('file:///test.html?testhooks=1', storage).window.__test.favList());
}

// ---------- scenario 1: normal boot + interactions ----------
const t0 = Date.now();
const app = boot();
const q = app.byId('q'), list = app.byId('list'), detail = app.byId('detail');
const type = (v) => { q.value = v; q.fire('input'); };
const nItems = () => (list.innerHTML.match(/class="item/g) || []).length;
const h1 = () => (detail.innerHTML.match(/<h1>([^<]*)<\/h1>/) || [, '(无)'])[1];
const results = [];
const check = (name, ok, extra) => { results.push([ok, name, extra]); console.log((ok ? 'PASS ' : 'FAIL ') + name + (extra ? '  · ' + extra : '')); };

check('boot: header stats', /\d{2},\d{3} 词/.test(app.byId('hdr-stat').textContent), app.byId('hdr-stat').textContent);
check('boot: welcome shown', detail.innerHTML.includes('随手一查'));

type('delve');
check('delve: card has word+释义', h1() === 'delve' && detail.innerHTML.includes('探究'));
const dictionary = app.window.__DICT__;
check('词库来源标记为 ECDICT 与 CMUdict', dictionary.src.includes('ECDICT') && dictionary.src.includes('CMUdict'));
check('开源词库无教材或精讲字段', dictionary.rows.length === dictionary.n
  && dictionary.rows.every(row => row.length === 20 && row.slice(10).every(value => value === 0 || value === '')));
check('delve: 不显示教材来源或精讲内容', !/Word List|记忆点|高频搭配/.test(detail.innerHTML));
check('delve: phonetic sanitised', /\[delv\]/.test(detail.innerHTML), (detail.innerHTML.match(/\[[^\]]+\]/) || [''])[0]);

type('探究');
check('中文反查: 探究 → delve', list.innerHTML.includes('delve'), nItems() + ' results');

type('dlve');
check('拼错容错: dlve 首选 delve', h1() === 'delve' && list.innerHTML.includes('delve'), h1());

type('gave');
check('变形还原: gave → give', list.innerHTML.includes('give') && detail.innerHTML.includes('是'));
check('变形还原: 标注过去式', /过去式/.test(detail.innerHTML), (detail.innerHTML.match(/「gave」[^<]*<b>[^<]*<\/b>[^<]*/) || [''])[0]);

type('del');
check('前缀 del → 多结果', nItems() >= 5, nItems() + ' results');

type('photosynthesis');
check('长难词: 有音标', /ˌ|ˈ/.test(detail.innerHTML));

type('zzzqqq');
check('无结果: 显示提示', detail.innerHTML.includes('没有找到'));

type('in');
check('短查询不卡（上限 300 条）', nItems() === 300, nItems() + ' rendered');

type('');
check('清空: 回到欢迎页', detail.innerHTML.includes('随手一查'));

// pronunciation
type('delve');
app.byId('speakUK').onclick();
app.byId('speakUS').onclick();
const p1 = app.played.find(u => /type=1/.test(u) && /audio=delve/.test(u));
const p2 = app.played.find(u => /type=2/.test(u) && /audio=delve/.test(u));
check('发音: 英音请求正确', !!p1, p1);
check('发音: 美音请求正确', !!p2, p2);
check('发音: 记录当前发音类型', app.byId('speakUS')._type === 2);

// favourite round-trip
type('delve');
app.byId('favBtn').onclick();
check('收藏写入 localStorage，重新打开仍存在', savedFavorites(app.storage).includes('delve'));
app.byId('favBtn').onclick();
check('再次点击取消收藏，重新打开已移除', savedFavorites(app.storage).length === 0);

// passage mode
// ---------- 查词准确性 ----------
function itemWords(){ return [...app.byId('list').innerHTML.matchAll(/class="w">([^<]+)</g)].map(m => m[1]); }
type('constructor');
check('constructor 不再崩，且命中词条', h1() === 'constructor', h1());
type('delve,');
check('delve, 清洗标点后命中 delve', h1() === 'delve', h1());
type('"delve"');
check('带引号查询也能命中', h1() === 'delve', h1());
type('were');
check('were 还原到 be', h1() === 'be', h1());
type('lives');
check('lives 同时给出 life / live', itemWords().includes('life') && itemWords().includes('live'), itemWords().slice(0,4).join(','));

type('ate');
const note = ((detail.innerHTML.match(/form-note">([\s\S]*?)<\/div>/) || [, ''])[1] || '').replace(/<[^>]+>/g, '');
check('词形标签: ate 标成过去式而不是过去分词', /过去式/.test(note) && !/过去分词/.test(note), note);

// ---------- 整段分析 ----------
const groups = (h) => {
  const out = { miss: [], book: [], known: [] };
  const re = /class="tok (new|book|known)" data-word="([^"]*)"/g;
  let m;
  while ((m = re.exec(h))) (m[1] === 'new' ? out.miss : m[1] === 'book' ? out.book : out.known).push(m[2]);
  return out;
};
app.byId('ptext').value = 'The prototype of the scheme is ubiquitous, and zzqxv matters.';
app.byId('m-pass').onclick();
check('整段: 切换到段落模式', app.window.document.body.classList.contains('pass-mode'));
app.byId('ptext').fire('input');

setTimeout(() => {
  const res = app.byId('passage-res').innerHTML;
  const g = groups(res);
  check('整段: 分出两组', res.includes('未收录') && !res.includes('书内词') && res.includes('词典收录'));
  check('整段: 已收录词归到「词典收录」组', g.known.includes('prototype'), 'known=[' + g.known.join(',') + ']');
  // 用一个确定不在词库里的人造词，只断言 miss 组
  check('整段: 未收录词只落在「未收录」组', g.miss.includes('zzqxv') && !g.book.includes('zzqxv') && !g.known.includes('zzqxv'),
    'miss=[' + g.miss.join(',') + ']');
  // 点已收录的词 → 打开对应词条
  app.byId('passage').fire('click', { target: { closest: () => ({ dataset: { word: 'prototype' } }) } });
  check('整段: 点词打开正确词条', h1() === 'prototype', h1());

  // 第二段：弯引号与两字母缩写
  app.byId('ptext').value = 'Don’t ignore the AI and UK data, the constructor and toString.';
  app.byId('ptext').fire('input');

  setTimeout(() => {
    const g2 = groups(app.byId('passage-res').innerHTML);
    const all = [].concat(g2.miss, g2.book, g2.known);
    check('分词: don’t 不拆成 don', !all.includes('don'), all.join(','));
    check('分词: don’t 保留为一个词', all.includes("don't"), all.join(','));
    check('分词: AI / UK 两字母缩写不丢', all.includes('ai') && all.includes('uk'), all.join(','));
    check('分词: constructor 不被去重表吃掉', all.includes('constructor'), all.join(','));

    // ---------- scenario 2: deep link ----------
    const dl = boot('file:///D:/dict/%E9%9B%85%E6%80%9D%E6%9F%A5%E8%AF%8D.html?q=delve');
    check('深链 ?q=delve 自动查词', dl.byId('q').value === 'delve' && dl.byId('detail').innerHTML.includes('探究'));

    // ---------- scenario 3: 划词弹窗 ----------
    const pp = boot('file:///D:/dict/%E9%9B%85%E6%80%9D%E6%9F%A5%E8%AF%8D.html?q=ubiquitous&popup=1');
    check('弹窗: body 带 popup 类', pp.window.document.body.classList.contains('popup'));
    check('弹窗: 单词直接出卡片', pp.byId('detail').innerHTML.includes('ubiquitous'));
    // 回归：小窗宽度 <=820px 时媒体查询会隐藏 #detail，popup 样式必须把它显回来
    check('弹窗样式: 小窗下详情区可见', /body\.popup #detail\{display:block/.test(html));

    const phrase = boot('file:///D:/dict/x.html?q=' + encodeURIComponent('due to') + '&popup=1');
    check('弹窗: 短语先查词条（due to）', phrase.byId('detail').innerHTML.includes('<h1>due to</h1>'), phrase.byId('detail').innerHTML.slice(0, 50));

    const ps = boot('file:///D:/dict/x.html?q=' + encodeURIComponent('Digital media is ubiquitous, and zzqxv matters') + '&popup=1');
    const ph = ps.byId('detail').innerHTML;
    check('弹窗: 整句走生词分析', ph.includes('选中') && ph.includes('点词看释义'), ph.slice(0, 60));
    const popupGroups = (h) => {
      const sec = (name) => {
        const tail = h.split('<h3>' + name + '</h3>')[1];
        if (!tail) return [];
        const body = tail.split('</div>')[0];
        return [...body.matchAll(/data-word="([^"]*)"/g)].map(m => m[1]);
      };
      return { miss: sec('未收录'), book: sec('书内词'), known: sec('词典收录') };
    };
    const pg = popupGroups(ph);
    check('弹窗: 分组标签与内容一致',
      pg.miss.includes('zzqxv') && pg.known.includes('ubiquitous') && !ph.includes('书内词'),
      'miss=[' + pg.miss.join(',') + '] known=[' + pg.known.join(',') + ']');
    ps.byId('detail').fire('click', { target: { closest: () => ({ dataset: { word: 'media' } }) } });

    // ---------- 生词本 ----------
    const emptyTab = boot('file:///D:/dict/x.html');
    check('生词本: 空库也显示导入按钮',
      emptyTab.byId('list').innerHTML.includes('id="impFav"'),
      emptyTab.byId('list').innerHTML.slice(0, 80));

    const shared = {};
    // 两个页面先都打开，再操作（模拟真实的多标签顺序）
    const tabA = boot('file:///D:/dict/x.html', shared);
    const tabB = boot('file:///D:/dict/x.html', shared);
    tabA.byId('q').value = 'delve'; tabA.byId('q').fire('input');
    tabB.byId('q').value = 'delve'; tabB.byId('q').fire('input');
    tabA.byId('favBtn').onclick();          // A 先收藏 delve
    // B 的按钮还是旧的「加入」，点它不应该删掉 A 刚收藏的
    tabB.byId('favBtn').onclick();
    let saved = savedFavorites(shared);
    check('生词本: 另一页的旧按钮不会删掉收藏', saved.includes('delve'), JSON.stringify(saved));

    tabB.byId('q').value = 'media'; tabB.byId('q').fire('input');
    tabB.byId('favBtn').onclick();
    saved = savedFavorites(shared);
    check('生词本: 两个页面先后收藏都保留', saved.includes('delve') && saved.includes('media'), JSON.stringify(saved));

    // constructor 这类名字不能被当成"已收藏"
    const ctor = boot('file:///D:/dict/x.html', {});
    ctor.byId('q').value = 'constructor'; ctor.byId('q').fire('input');
    const ctorBtn = ctor.byId('favBtn');
    check('生词本: constructor 初始未收藏', ctorBtn.dataset.on === '0', ctorBtn.dataset.on);
    ctorBtn.onclick();
    check('生词本: constructor 能真正存进去', savedFavorites(ctor.storage).includes('constructor'));

    // 写入失败时不能谎报成功
    const failTab = boot('file:///D:/dict/x.html', { __failWrites: true });
    failTab.byId('q').value = 'delve'; failTab.byId('q').fire('input');
    const badBtn = failTab.byId('favBtn');
    badBtn.onclick();
    check('生词本: 写入失败时不谎报成功',
      /保存失败/.test(failTab.byId('toast').textContent) && badBtn.dataset.on === '0',
      failTab.byId('toast').textContent + ' / data-on=' + badBtn.dataset.on);

    // 导入：成功 / 写入失败两条路
    const imp = boot('file:///D:/dict/x.html?testhooks=1', {});
    const r1 = imp.window.__test.applyImported(['alpha', 'beta']);
    check('生词本: 导入成功并落盘', r1.ok && r1.added === 2 && savedFavorites(imp.storage).includes('alpha'), JSON.stringify(r1));

    const impFail = boot('file:///D:/dict/x.html?testhooks=1', { __failWrites: true });
    const r2 = impFail.window.__test.applyImported(['alpha', 'beta']);
    check('生词本: 导入写入失败时明确失败', r2.ok === false && r2.added === 0 && savedFavorites(impFail.storage).length === 0, JSON.stringify(r2));

    // A 已读旧状态、尚未保存时，B 抢先完成保存，再让 A 提交。
    const concurrent = {};
    const concurrentA = boot('file:///test.html', concurrent);
    const concurrentB = boot('file:///test.html', concurrent);
    concurrentA.byId('q').value = 'delve'; concurrentA.byId('q').fire('input');
    concurrentB.byId('q').value = 'media'; concurrentB.byId('q').fire('input');
    concurrent.__beforeWrite = function(key) {
      if (!key.startsWith('ielts.dict.fav.op.')) return;
      delete concurrent.__beforeWrite;
      concurrentB.byId('favBtn').onclick();
    };
    concurrentA.byId('favBtn').onclick();
    const simultaneous = savedFavorites(concurrent);
    check('生词本: 两页面交错写入后重新打开都保留',
      simultaneous.length === 2 && simultaneous.includes('delve') && simultaneous.includes('media'), JSON.stringify(simultaneous));

    const legacy = {'ielts.dict.fav':'["delve","media"]'};
    const legacyTab = boot('file:///test.html', legacy);
    legacyTab.byId('q').value = 'delve'; legacyTab.byId('q').fire('input');
    legacyTab.byId('favBtn').onclick();
    const restoredLegacy = savedFavorites(legacy);
    check('生词本: 旧版数组可读取且取消收藏不会复活',
      restoredLegacy.length === 1 && restoredLegacy[0] === 'media', JSON.stringify(restoredLegacy));

    const sync = {};
    const syncA = boot('file:///test.html', sync);
    const syncB = boot('file:///test.html', sync);
    syncA.byId('q').value = 'delve'; syncA.byId('q').fire('input');
    syncB.byId('q').value = 'delve'; syncB.byId('q').fire('input');
    syncA.byId('favBtn').onclick();
    syncB.fireStorage(Object.keys(sync).find(key => key.startsWith('ielts.dict.fav.op.')));
    check('生词本: 保存记录事件同步另一页面按钮', syncB.byId('favBtn').dataset.on === '1');

    const conflict = {'ielts.dict.fav':'["delve"]'};
    const removeTab = boot('file:///test.html', conflict);
    const addTab = boot('file:///test.html', conflict);
    removeTab.byId('q').value = 'delve'; removeTab.byId('q').fire('input');
    addTab.byId('q').value = 'media'; addTab.byId('q').fire('input');
    conflict.__beforeWrite = function() {
      delete conflict.__beforeWrite;
      addTab.byId('favBtn').onclick();
    };
    removeTab.byId('favBtn').onclick();
    const conflictResult = savedFavorites(conflict);
    check('生词本: 交错取消和新增互不覆盖', conflictResult.length === 1 && conflictResult[0] === 'media', JSON.stringify(conflictResult));

    const invalidImport = boot('file:///D:/dict/x.html?testhooks=1', {});
    const invalidValues = [null, {}, 123, true, ['nested'], 'a'.repeat(201), 'line\nbreak', 'with\ttab', 'nul\u0000'];
    const rejected = invalidValues.every(value => {
      const result = invalidImport.window.__test.applyImported(['delve', value]);
      return result.ok === false && result.added === 0 && invalidImport.window.__test.favList().length === 0;
    });
    check('生词本: 无效导入整批拒绝且不部分保存', rejected);

    const normalizedImport = boot('file:///D:/dict/x.html?testhooks=1', {});
    const normalizedResult = normalizedImport.window.__test.applyImported([' DELVE ', 'delve', 'Custom   Phrase', 'CUSTOM PHRASE', 'don’t']);
    const normalizedWords = Array.from(normalizedImport.window.__test.favList());
    check('生词本: 导入规范化去重并保留未收录词',
      normalizedResult.ok && normalizedResult.added === 3 && normalizedWords.includes('delve')
      && normalizedWords.includes('custom phrase') && normalizedWords.includes("don't"), JSON.stringify(normalizedWords));

    const legacyCsvWords = ['=1+1', '+SUM(1,2)', '-1+1', '@SUM(1,2)', '  =1+1', '＝1+1', 'two,columns', 'a"quote', 'line\nbreak', '\u0000=1+1'];
    const csvExport = boot('file:///D:/dict/x.html?testhooks=1', {'ielts.dict.fav': JSON.stringify(legacyCsvWords)});
    csvExport.byId('list').fire('click', {target: {id:'expCsv'}});
    const csvText = csvExport.downloads[0].parts.join('');
    const csvRows = csvText.replace(/^\uFEFF/, '').split('\r\n');
    const csvCells = csvRows.slice(1).map(row => [...row.matchAll(/"((?:""|[^"])*)"(?:,|$)/g)].map(m => m[1].replace(/""/g, '"')));
    check('生词本: CSV 未收录词也转义且行列完整',
      csvRows.length === legacyCsvWords.length + 1 && csvCells.every(row => row.length === 6)
      && csvCells[6][0] === 'two,columns' && csvCells[7][0] === 'a"quote' && csvCells[8][0] === 'line break');
    check('生词本: CSV 公式前缀作为文本导出', csvCells.slice(0,6).every(row => row[0].startsWith('\t')));
    check('生词本: 旧收藏中的控制字符不能绕过公式防护', csvCells[9][0].startsWith('\t') && !csvCells[9][0].includes('\u0000'));

    const backupSource = boot('file:///test.html', {'ielts.dict.fav':'["delve","media"]'});
    backupSource.byId('list').fire('click', {target: {id:'expJson'}});
    const backupContents = backupSource.downloads[0].parts.join('');
    const backupTarget = boot('file:///test.html');
    backupTarget.window.importContents = backupContents;
    backupTarget.byId('list').fire('click', {target: {id:'impFav'}});
    const backupRestored = savedFavorites(backupTarget.storage);
    check('生词本: 实际导出 JSON 经文件入口恢复空库',
      backupRestored.length === 2 && backupRestored.includes('delve') && backupRestored.includes('media')
      && /导入完成/.test(backupTarget.byId('toast').textContent), JSON.stringify(backupRestored));

    const backupFailed = boot('file:///test.html', {__failWrites:true});
    backupFailed.window.importContents = backupContents;
    backupFailed.byId('list').fire('click', {target: {id:'impFav'}});
    check('生词本: 文件导入写入失败时提示失败且不部分保存',
      /保存失败/.test(backupFailed.byId('toast').textContent) && savedFavorites(backupFailed.storage).length === 0);

    const invalidFile = boot('file:///test.html');
    invalidFile.window.importContents = '["delve",null]';
    invalidFile.byId('list').fire('click', {target: {id:'impFav'}});
    check('生词本: 文件导入非法内容时说明原因并保持空库',
      /无效词条/.test(invalidFile.byId('toast').textContent) && savedFavorites(invalidFile.storage).length === 0);

    // 弹窗里也要能看到"相近拼写"提示
    const fz = boot('file:///D:/dict/x.html?q=dlve&popup=1');
    check('弹窗: 相近拼写有提示', /相近拼写/.test(fz.byId('detail').innerHTML), fz.byId('detail').innerHTML.slice(0, 80));
    const tabC = boot('file:///D:/dict/x.html', shared);
    check('生词本: 有内容时显示 CSV / JSON 导出',
      tabC.byId('list').innerHTML.includes('id="expCsv"') && tabC.byId('list').innerHTML.includes('id="expJson"'));

    const bad = results.filter(r => !r[0]);
    console.log('\n' + (bad.length ? `❌ ${bad.length} failed of ${results.length}` : `✅ all ${results.length} checks passed`) + ` · ${Date.now() - t0}ms`);
    process.exit(bad.length ? 1 : 0);
  }, 600);
}, 600);
