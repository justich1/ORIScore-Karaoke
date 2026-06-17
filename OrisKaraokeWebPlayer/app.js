(() => {
  const $ = (id) => document.getElementById(id);

  const state = {
    songs: [],
    filtered: [],
    currentIndex: -1,
    currentSong: null,
    currentJson: null,
    offsetMs: Number(localStorage.getItem('orisKaraokeOffsetMs') || '0'),
    timer: null
  };

  const audio = $('audio');

  const els = {
    btnOpenFolder: $('btnOpenFolder'),
    btnOpenFiles: $('btnOpenFiles'),
    filePicker: $('filePicker'),
    search: $('search'),
    list: $('list'),
    btnPrev: $('btnPrev'),
    btnPlay: $('btnPlay'),
    btnNext: $('btnNext'),
    btnFull: $('btnFull'),
    btnOffsetMinus: $('btnOffsetMinus'),
    btnOffsetPlus: $('btnOffsetPlus'),
    offsetText: $('offsetText'),
    countText: $('countText'),
    missingText: $('missingText'),
    statusText: $('statusText'),
    title: $('title'),
    artist: $('artist'),
    time: $('time'),
    prevLine: $('prevLine'),
    currentLine: $('currentLine'),
    nextLine: $('nextLine')
  };

  init();

  function init() {
    updateOffsetText();

    els.btnOpenFolder.addEventListener('click', openFolder);
    els.btnOpenFiles.addEventListener('click', () => {
      els.filePicker.removeAttribute('webkitdirectory');
      els.filePicker.removeAttribute('directory');
      els.filePicker.click();
    });
    els.filePicker.addEventListener('change', openFiles);
    els.search.addEventListener('input', applyFilter);

    els.btnPrev.addEventListener('click', prevSong);
    els.btnPlay.addEventListener('click', togglePlay);
    els.btnNext.addEventListener('click', nextSong);
    els.btnFull.addEventListener('click', toggleFullscreen);
    els.btnOffsetMinus.addEventListener('click', () => changeOffset(-100));
    els.btnOffsetPlus.addEventListener('click', () => changeOffset(100));

    audio.addEventListener('timeupdate', renderByAudio);
    audio.addEventListener('ended', onSongEnded);
    audio.addEventListener('play', startRenderTimer);
    audio.addEventListener('pause', stopRenderTimer);
    audio.addEventListener('error', () => setStatus('Prohlížeč neumí načíst audio. Zkus MP3 nebo WAV PCM.'));

    setStatus(location.protocol === 'file:'
      ? 'Běžíš přes file://. Lepší je START_PLAYER.bat, jinak Chrome může blokovat některé věci.'
      : 'Vyber složku nebo soubory.');
  }

  async function openFolder() {
    if (!window.showDirectoryPicker) {
      setStatus('Tenhle režim neumí otevření složky. Použij START_PLAYER.bat v Chrome/Edge, nebo vyber soubory ručně.');
      els.filePicker.setAttribute('webkitdirectory', '');
      els.filePicker.setAttribute('directory', '');
      els.filePicker.click();
      return;
    }

    try {
      const root = await window.showDirectoryPicker();
      setStatus('Skenuji složku...');
      const files = [];
      await walkDirectory(root, '', files);
      await buildLibrary(files);
    } catch (e) {
      if (e && e.name !== 'AbortError') setStatus('Chyba otevření složky: ' + e.message);
    }
  }

  async function walkDirectory(dirHandle, path, out) {
    for await (const [name, handle] of dirHandle.entries()) {
      const full = path ? path + '/' + name : name;
      if (handle.kind === 'directory') {
        await walkDirectory(handle, full, out);
      } else {
        const file = await handle.getFile();
        file._orisPath = full;
        out.push(file);
      }
    }
  }

  async function openFiles(evt) {
    const files = Array.from(evt.target.files || []);
    files.forEach(f => f._orisPath = f.webkitRelativePath || f.name);
    await buildLibrary(files);
    evt.target.value = '';
  }

  async function buildLibrary(files) {
    cleanupObjectUrls();

    const audioFiles = files.filter(f => isAudioFile(f.name));
    const byLowerPath = new Map();
    const byLowerBase = new Map();
    const byKey = new Map();

    for (const f of files) {
      const p = normalizePath(f._orisPath || f.name);
      byLowerPath.set(p.toLowerCase(), f);
      byLowerBase.set(baseName(p).toLowerCase(), f);

      if (isAudioFile(f.name)) {
        const k = audioKey(baseName(p));
        if (!byKey.has(k)) byKey.set(k, []);
        byKey.get(k).push(f);
      }
    }

    // ORIScore karaoke:
    // .ock = nový ORIScore Karaoke soubor
    // .karaoke.json = starý export
    // .json = fallback pro testy / staré soubory
    const jsonFiles = files.filter(f =>
      /\.ock$/i.test(f.name)
      || /\.karaoke\.json$/i.test(f.name)
      || /\.json$/i.test(f.name)
    );

    const songs = [];

    for (const jf of jsonFiles) {
      try {
        const text = await jf.text();
        const doc = JSON.parse(text);

        const fmt = get(doc, 'format', 'Format') || '';
        const lines = get(doc, 'lines', 'Lines');
        const audioName = get(doc, 'audio', 'Audio') || '';

        if (!String(fmt).includes('ORIS_KARAOKE') && !Array.isArray(lines) && !audioName) {
          continue;
        }

        const jsonPath = normalizePath(jf._orisPath || jf.name);
        const audioFile = findAudioFile(audioName, jsonPath, byLowerPath, byLowerBase, byKey, audioFiles);

        songs.push({
          title: get(doc, 'title', 'Title') || stripKaraokeExt(jf.name),
          artist: get(doc, 'artist', 'Artist') || '',
          album: get(doc, 'album', 'Album') || '',
          jsonFile: jf,
          jsonPath,
          audioFile,
          audioName,
          doc,
          objectUrl: null
        });
      } catch (e) {
        console.warn('JSON/OCK skip/error', jf.name, e);
      }
    }

    songs.sort((a, b) => String(a.title).localeCompare(String(b.title), 'cs'));

    state.songs = songs;
    state.currentIndex = -1;
    state.currentSong = null;
    state.currentJson = null;
    audio.removeAttribute('src');

    applyFilter();

    const missing = songs.filter(s => !s.audioFile).length;
    setStatus(`Načteno ${songs.length} karaoke souborů. Audio nenalezeno: ${missing}.`);
  }

  function findAudioFile(audioName, jsonPath, byLowerPath, byLowerBase, byKey, audioFiles) {
    const candidates = [];

    if (audioName) {
      const clean = cleanupAudioName(audioName);
      candidates.push(clean);
      candidates.push(baseName(clean));
      candidates.push(dirName(jsonPath) + '/' + baseName(clean));
    }

    const jsonStem = stripKaraokeExt(baseName(jsonPath));

    for (const ext of ['.mp3', '.MP3', '.wav', '.WAV', '.m4a', '.M4A', '.ogg', '.OGG', '.wma', '.WMA']) {
      candidates.push(dirName(jsonPath) + '/' + jsonStem + ext);
      candidates.push(jsonStem + ext);
    }

    for (const c of candidates) {
      const norm = normalizePath(c);

      const byPath = byLowerPath.get(norm.toLowerCase());
      if (byPath && isAudioFile(byPath.name)) return byPath;

      const byBase = byLowerBase.get(baseName(norm).toLowerCase());
      if (byBase && isAudioFile(byBase.name)) return byBase;
    }

    const desiredKeys = [];

    if (audioName) {
      desiredKeys.push(audioKey(baseName(cleanupAudioName(audioName))));
    }

    desiredKeys.push(audioKey(jsonStem));

    for (const k of desiredKeys) {
      const direct = byKey.get(k);
      if (direct && direct.length) return direct[0];
    }

    // Fuzzy fallback: ignoruje mezery, pomlčky, diakritiku, číslování a příponu.
    let best = null;
    let bestScore = 0;
    const target = desiredKeys.find(Boolean) || audioKey(jsonStem);

    if (target) {
      for (const f of audioFiles) {
        const k = audioKey(f.name);
        const score = similarity(target, k);

        if (score > bestScore) {
          bestScore = score;
          best = f;
        }
      }
    }

    // Radši opatrně, ať to nespáruje úplně jinou skladbu.
    return bestScore >= 0.72 ? best : null;
  }

  function applyFilter() {
    const q = normalizeSearch(els.search.value.trim());

    state.filtered = state.songs.filter(s => {
      if (!q) return true;

      return normalizeSearch([
        s.title,
        s.artist,
        s.album,
        s.audioName,
        s.jsonPath,
        s.audioFile && (s.audioFile._orisPath || s.audioFile.name)
      ].join(' ')).includes(q);
    });

    renderList();
  }

  function renderList() {
    els.countText.textContent = state.filtered.length;
    els.missingText.textContent = state.songs.filter(s => !s.audioFile).length;
    els.list.innerHTML = '';

    for (const song of state.filtered) {
      const realIndex = state.songs.indexOf(song);

      const btn = document.createElement('button');
      btn.className = 'item'
        + (realIndex === state.currentIndex ? ' active' : '')
        + (!song.audioFile ? ' missing' : '');

      const audioLine = song.audioFile
        ? 'audio: ' + (song.audioFile._orisPath || song.audioFile.name)
        : 'audio nenalezeno: ' + (song.audioName || '(prázdné)');

      btn.innerHTML = `
        <div class="item-title">${escapeHtml(song.title || '(bez názvu)')}</div>
        <div class="item-meta">${escapeHtml(song.artist || '')}</div>
        <div class="item-meta ${song.audioFile ? '' : 'item-missing'}">${escapeHtml(audioLine)}</div>
      `;

      btn.addEventListener('click', () => playIndex(realIndex));
      els.list.appendChild(btn);
    }
  }

  async function playIndex(index) {
    if (index < 0 || index >= state.songs.length) return;

    const song = state.songs[index];

    if (!song.audioFile) {
      setStatus('Audio ke karaoke souboru nenalezeno: ' + (song.audioName || song.title));
      return;
    }

    cleanupCurrentObjectUrl();

    state.currentIndex = index;
    state.currentSong = song;
    state.currentJson = normalizeSongJson(song.doc);

    song.objectUrl = URL.createObjectURL(song.audioFile);
    audio.src = song.objectUrl;

    els.title.textContent = state.currentJson.title || song.title || 'Karaoke';
    els.artist.textContent = state.currentJson.artist || song.artist || '';

    renderList();
    renderByAudio();

    try {
      await audio.play();
      setStatus('Přehrávám.');
    } catch (e) {
      setStatus('Klikni na Play, prohlížeč zablokoval automatické spuštění.');
    }
  }

  function normalizeSongJson(doc) {
    const linesRaw = get(doc, 'lines', 'Lines') || [];

    const lines = linesRaw.map((line, lineIndex) => {
      const wordsRaw = get(line, 'words', 'Words') || [];

      const words = wordsRaw.map((w, wordIndex) => ({
        index: Number(get(w, 'index', 'Index') ?? wordIndex),
        text: String(get(w, 'text', 'Text') ?? ''),
        timeMs: Number(get(w, 'timeMs', 'TimeMs') ?? 0),
        endMs: Number(get(w, 'endMs', 'EndMs') ?? 0)
      }));

      return {
        index: Number(get(line, 'index', 'Index') ?? lineIndex),
        text: String(get(line, 'text', 'Text') ?? ''),
        startMs: Number(get(line, 'startMs', 'StartMs') ?? (words[0]?.timeMs ?? 0)),
        endMs: Number(get(line, 'endMs', 'EndMs') ?? (words[words.length - 1]?.endMs ?? 0)),
        words
      };
    });

    return {
      format: get(doc, 'format', 'Format') || 'ORIS_KARAOKE_V1',
      title: get(doc, 'title', 'Title') || '',
      artist: get(doc, 'artist', 'Artist') || '',
      album: get(doc, 'album', 'Album') || '',
      audio: get(doc, 'audio', 'Audio') || '',
      lines
    };
  }

  function renderByAudio() {
    const song = state.currentJson;
    const t = Math.max(0, Math.floor(audio.currentTime * 1000) + state.offsetMs);

    els.time.textContent = `${formatTime(audio.currentTime)} / ${formatTime(audio.duration || 0)}`;

    if (!song || !song.lines || song.lines.length === 0) {
      els.prevLine.textContent = '';
      els.currentLine.textContent = state.currentSong ? 'Tahle skladba nemá karaoke text' : 'Karaoke';
      els.nextLine.textContent = '';
      return;
    }

    let idx = song.lines.findIndex((line, i) => {
      const next = song.lines[i + 1];
      const start = Number(line.startMs || 0);
      const end = next ? Number(next.startMs || 0) : Number(line.endMs || start + 5000);

      return t >= start && t < end;
    });

    if (idx < 0) {
      idx = t < Number(song.lines[0].startMs || 0)
        ? 0
        : song.lines.length - 1;
    }

    els.prevLine.textContent = idx > 0 ? song.lines[idx - 1].text : '';
    renderCurrentLine(song.lines[idx], t);
    els.nextLine.textContent = idx + 1 < song.lines.length ? song.lines[idx + 1].text : '';
  }

  function renderCurrentLine(line, t) {
    if (!line.words || line.words.length === 0) {
      els.currentLine.textContent = line.text || '';
      return;
    }

    const parts = [];

    for (let i = 0; i < line.words.length; i++) {
      const w = line.words[i];
      const next = line.words[i + 1];

      const start = Number(w.timeMs || 0);
      const end = Number(w.endMs || (next ? next.timeMs : line.endMs || start + 300));

      let cls = 'future';

      if (t >= end) cls = 'past';
      else if (t >= start && t < end) cls = 'now';

      parts.push(`<span class="word ${cls}">${escapeHtml(w.text || '')}</span>`);
    }

    els.currentLine.innerHTML = parts.join(' ');
  }

  function nextSong() {
    if (!state.songs.length) return;

    let idx = state.currentIndex + 1;

    if (idx >= state.songs.length)
      idx = 0;

    playIndex(idx);
  }

  function prevSong() {
    if (!state.songs.length) return;

    let idx = state.currentIndex - 1;

    if (idx < 0)
      idx = state.songs.length - 1;

    playIndex(idx);
  }

  function onSongEnded() {
    stopRenderTimer();
    renderByAudio();
    setStatus('Skladba dohrála. Další se nespouští automaticky.');
  }

  function togglePlay() {
    if (!audio.src) {
      if (state.filtered.length) {
        playIndex(state.songs.indexOf(state.filtered[0]));
      }

      return;
    }

    if (audio.paused) audio.play();
    else audio.pause();
  }

  function toggleFullscreen() {
    const root = document.documentElement;

    if (!document.fullscreenElement) {
      root.requestFullscreen?.();
      document.body.classList.add('fullscreen');
    } else {
      document.exitFullscreen?.();
      document.body.classList.remove('fullscreen');
    }
  }

  document.addEventListener('fullscreenchange', () => {
    document.body.classList.toggle('fullscreen', !!document.fullscreenElement);
  });

  function changeOffset(delta) {
    state.offsetMs += delta;
    localStorage.setItem('orisKaraokeOffsetMs', String(state.offsetMs));
    updateOffsetText();
    renderByAudio();
  }

  function updateOffsetText() {
    els.offsetText.textContent = `${state.offsetMs} ms`;
  }

  function startRenderTimer() {
    stopRenderTimer();
    state.timer = setInterval(renderByAudio, 50);
  }

  function stopRenderTimer() {
    if (state.timer) clearInterval(state.timer);
    state.timer = null;
  }

  function cleanupCurrentObjectUrl() {
    const s = state.currentSong;

    if (s && s.objectUrl) {
      URL.revokeObjectURL(s.objectUrl);
      s.objectUrl = null;
    }
  }

  function cleanupObjectUrls() {
    for (const s of state.songs) {
      if (s.objectUrl) {
        URL.revokeObjectURL(s.objectUrl);
        s.objectUrl = null;
      }
    }
  }

  function setStatus(s) {
    els.statusText.textContent = s;
  }

  function get(obj, ...names) {
    for (const n of names) {
      if (obj && Object.prototype.hasOwnProperty.call(obj, n)) {
        return obj[n];
      }
    }

    return undefined;
  }

  function cleanupAudioName(s) {
    s = String(s || '').trim();
    s = s.replace(/^(\d+,)?[LRMS],/i, '');
    s = s.replace(/\\/g, '/');
    return s;
  }

  function normalizePath(p) {
    return String(p || '')
      .replace(/\\/g, '/')
      .replace(/^\/+/, '')
      .replace(/\/+/g, '/');
  }

  function baseName(p) {
    p = normalizePath(p);
    return p.split('/').pop() || p;
  }

  function dirName(p) {
    p = normalizePath(p);

    const i = p.lastIndexOf('/');

    return i >= 0 ? p.slice(0, i) : '';
  }

  function stripKaraokeExt(name) {
    return String(name || '')
      .replace(/\.karaoke\.json$/i, '')
      .replace(/\.ock$/i, '')
      .replace(/\.json$/i, '');
  }

  function isAudioFile(name) {
    return /\.(mp3|wav|m4a|ogg|wma)$/i.test(String(name || ''));
  }

  function normalizeSearch(s) {
    return String(s || '')
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .toLowerCase();
  }

  function audioKey(name) {
    return normalizeSearch(stripExt(baseName(name)))
      .replace(/\b(playback|karaoke|original|music|audio)\b/g, '')
      .replace(/^\d+[\s._-]*/g, '')
      .replace(/[\s._()[\]{}+-]+/g, '')
      .replace(/[^a-z0-9]/g, '');
  }

  function stripExt(name) {
    return String(name || '').replace(/\.[^.]+$/, '');
  }

  function similarity(a, b) {
    if (!a || !b) return 0;

    if (a === b) return 1;

    if (a.includes(b) || b.includes(a)) {
      return Math.min(a.length, b.length) / Math.max(a.length, b.length);
    }

    const dist = levenshtein(a, b);

    return 1 - dist / Math.max(a.length, b.length);
  }

  function levenshtein(a, b) {
    const m = a.length;
    const n = b.length;
    const dp = new Array(n + 1);

    for (let j = 0; j <= n; j++)
      dp[j] = j;

    for (let i = 1; i <= m; i++) {
      let prev = dp[0];
      dp[0] = i;

      for (let j = 1; j <= n; j++) {
        const tmp = dp[j];

        dp[j] = Math.min(
          dp[j] + 1,
          dp[j - 1] + 1,
          prev + (a[i - 1] === b[j - 1] ? 0 : 1)
        );

        prev = tmp;
      }
    }

    return dp[n];
  }

  function formatTime(sec) {
    if (!Number.isFinite(sec))
      sec = 0;

    sec = Math.max(0, Math.floor(sec));

    const m = Math.floor(sec / 60);
    const s = sec % 60;

    return String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, ch => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    }[ch]));
  }
})();