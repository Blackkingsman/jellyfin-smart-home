/* Smart Home: Netflix-style rows on the Jellyfin home screen.
 * Runs in the web client as the signed-in user, so every row is personal. */
(function () {
  'use strict';

  const REFRESH_MS = 15 * 60 * 1000;
  let building = false;

  // ── helpers ──

  const api = () => window.ApiClient;

  function get(path, params) {
    return api().getJSON(api().getUrl(path, params));
  }

  // deterministic shuffle: same order all day for a user, new order tomorrow
  function seeded(seedText) {
    let h = 1779033703 ^ seedText.length;
    for (let i = 0; i < seedText.length; i++) {
      h = Math.imul(h ^ seedText.charCodeAt(i), 3432918353);
      h = (h << 13) | (h >>> 19);
    }
    return function () {
      h = Math.imul(h ^ (h >>> 16), 2246822507);
      h = Math.imul(h ^ (h >>> 13), 3266489909);
      return ((h ^= h >>> 16) >>> 0) / 4294967296;
    };
  }

  function shuffle(items, rand) {
    const a = items.slice();
    for (let i = a.length - 1; i > 0; i--) {
      const j = Math.floor(rand() * (i + 1));
      [a[i], a[j]] = [a[j], a[i]];
    }
    return a;
  }

  const unplayedFirst = (items) => items.filter((i) => !i.UserData?.Played).concat(items.filter((i) => i.UserData?.Played));

  const FIELDS = { Fields: 'Genres,BackdropImageTags,PrimaryImageAspectRatio', EnableImageTypes: 'Primary,Backdrop,Thumb', ImageTypeLimit: 1 };

  // ── rows ──

  // last few things watched, with episodes standing in for their show
  async function recentSeeds(uid, count) {
    const recent = await get('Items', {
      userId: uid, IncludeItemTypes: 'Movie,Episode', Recursive: true, Filters: 'IsPlayed',
      SortBy: 'DatePlayed', SortOrder: 'Descending', Limit: 40,
    });
    const seen = new Set();
    const seeds = [];
    for (const item of recent.Items) {
      const seed = item.Type === 'Episode' ? { Id: item.SeriesId, Name: item.SeriesName, Type: 'Series' } : item;
      if (!seed.Id || seen.has(seed.Id)) continue;
      seen.add(seed.Id);
      seeds.push(seed);
      if (seeds.length === count) break;
    }
    return seeds;
  }

  async function becauseYouWatched(uid, limit) {
    const seeds = await recentSeeds(uid, 3);
    return Promise.all(seeds.map(async (seed) => {
      const similar = await get(`Items/${seed.Id}/Similar`, { userId: uid, Limit: limit * 2, ...FIELDS });
      return { title: `Because you watched ${seed.Name}`, items: similar.Items.filter((i) => !i.UserData?.Played).slice(0, limit) };
    }));
  }

  const favoriteGenre = (items) => {
    const counts = {};
    items.forEach((m) => (m.Genres || []).forEach((g) => (counts[g] = (counts[g] || 0) + 1)));
    return Object.keys(counts).sort((a, b) => counts[b] - counts[a])[0];
  };

  // the genre you watch most, best-rated titles you haven't seen
  async function topPicks(uid, limit, rand) {
    const [movies, episodes] = await Promise.all(['Movie', 'Episode'].map((type) => get('Items', {
      userId: uid, IncludeItemTypes: type, Recursive: true, Filters: 'IsPlayed',
      SortBy: 'DatePlayed', SortOrder: 'Descending', Limit: 40, Fields: 'Genres',
    })));
    const showIds = [...new Set(episodes.Items.map((e) => e.SeriesId).filter(Boolean))];
    const shows = showIds.length ? (await get('Items', { userId: uid, Ids: showIds.join(','), Fields: 'Genres' })).Items : [];

    const rows = [];
    for (const [type, history, label] of [['Movie', movies.Items, 'Top picks for you'], ['Series', shows, 'TV picks for you']]) {
      const favorite = favoriteGenre(history);
      if (!favorite) continue;
      const pool = await get('Items', {
        userId: uid, IncludeItemTypes: type, Recursive: true, Genres: favorite, Filters: 'IsUnplayed',
        SortBy: 'CommunityRating', SortOrder: 'Descending', Limit: 80, ...FIELDS,
      });
      rows.push({ title: `${label}: ${favorite}`, items: shuffle(pool.Items, rand).slice(0, limit) });
    }
    return rows;
  }

  async function newArrivals(uid, limit) {
    const today = new Date().toISOString();
    const [released, added] = await Promise.all([
      // actually new: released recently, newest first (MaxPremiereDate drops placeholder future dates)
      get('Items', {
        userId: uid, IncludeItemTypes: 'Movie,Series', Recursive: true, MaxPremiereDate: today,
        SortBy: 'PremiereDate', SortOrder: 'Descending', Limit: limit, ...FIELDS,
      }),
      // new to this server, whatever year it came out
      get('Items', {
        userId: uid, IncludeItemTypes: 'Movie,Series', Recursive: true,
        SortBy: 'DateCreated', SortOrder: 'Descending', Limit: limit, ...FIELDS,
      }),
    ]);
    return [{ title: 'New releases', items: released.Items }, { title: 'Recently added', items: added.Items }];
  }

  async function shelves(uid, count, limit, rand) {
    const all = await get('Items', { userId: uid, IncludeItemTypes: 'BoxSet', Recursive: true });
    const picked = shuffle(all.Items, rand).slice(0, count);
    return Promise.all(picked.map(async (shelf) => {
      const kids = await get('Items', { userId: uid, ParentId: shelf.Id, Limit: 200, ...FIELDS });
      return { title: shelf.Name, link: shelf.Id, items: unplayedFirst(shuffle(kids.Items, rand)).slice(0, limit) };
    }));
  }

  // ── rendering ──

  function imageUrl(item) {
    if (item.BackdropImageTags?.length) {
      return api().getImageUrl(item.Id, { type: 'Backdrop', tag: item.BackdropImageTags[0], maxWidth: 560, quality: 85 });
    }
    const tags = item.ImageTags || {};
    const type = tags.Thumb ? 'Thumb' : 'Primary';
    return tags[type] ? api().getImageUrl(item.Id, { type, tag: tags[type], maxWidth: 560, quality: 85 }) : '';
  }

  const detailsHref = (id) => `#/details?id=${id}&serverId=${api().serverId()}`;

  function renderRow(row) {
    const section = document.createElement('div');
    section.className = 'sh-row verticalSection';

    const title = document.createElement(row.link ? 'a' : 'h2');
    title.className = 'sh-title sectionTitle sectionTitle-cards';
    title.textContent = row.title;
    if (row.link) title.href = detailsHref(row.link);
    section.appendChild(title);

    const wrap = document.createElement('div');
    wrap.className = 'sh-wrap';
    const scroller = document.createElement('div');
    scroller.className = 'sh-scroller';
    row.items.forEach((item) => {
      const card = document.createElement('a');
      card.className = 'sh-card';
      card.href = detailsHref(item.Id);
      card.title = item.Name;
      const img = imageUrl(item);
      if (img) card.style.backgroundImage = `url("${img}")`;
      const label = document.createElement('span');
      label.textContent = item.Name;
      card.appendChild(label);
      scroller.appendChild(card);
    });

    ['prev', 'next'].forEach((dir) => {
      const btn = document.createElement('button');
      btn.className = `sh-arrow sh-${dir}`;
      btn.type = 'button';
      btn.setAttribute('aria-label', dir === 'prev' ? 'Scroll left' : 'Scroll right');
      btn.textContent = dir === 'prev' ? '‹' : '›';
      btn.addEventListener('click', () => scroller.scrollBy({ left: (dir === 'prev' ? -1 : 1) * scroller.clientWidth * 0.85, behavior: 'smooth' }));
      wrap.appendChild(btn);
    });
    wrap.appendChild(scroller);
    section.appendChild(wrap);
    return section;
  }

  // Netflix interleaves personal rows with themed ones
  function interleave(personal, themed) {
    const out = [];
    while (personal.length || themed.length) {
      if (personal.length) out.push(personal.shift());
      if (themed.length) out.push(themed.shift());
      if (themed.length) out.push(themed.shift());
    }
    return out;
  }

  async function build(homeTab) {
    if (building) return;
    building = true;
    try {
      const uid = api().getCurrentUserId();
      const cfg = await get('SmartHome/config').catch(() => ({ ShelfRows: 8, ItemsPerRow: 20 }));
      const rand = seeded(new Date().toDateString() + uid);
      const results = await Promise.allSettled([
        becauseYouWatched(uid, cfg.ItemsPerRow),
        topPicks(uid, cfg.ItemsPerRow, rand),
        newArrivals(uid, cfg.ItemsPerRow),
        shelves(uid, cfg.ShelfRows, cfg.ItemsPerRow, rand),
      ]);
      const [byw, picks, fresh, themed] = results.map((r) => (r.status === 'fulfilled' ? r.value : []));
      results.filter((r) => r.status === 'rejected').forEach((r) => console.warn('[SmartHome]', r.reason));

      const [released, added] = fresh;
      const rows = interleave([released, ...byw, ...picks, added].filter(Boolean), themed).filter((r) => r.items.length >= 4);

      let container = homeTab.querySelector(':scope > .smartHome');
      if (!container) {
        container = document.createElement('div');
        container.className = 'smartHome';
        // a sibling of .sections, which Jellyfin re-renders from scratch
        homeTab.insertBefore(container, homeTab.querySelector('.sections'));
      }
      container.replaceChildren(...rows.map(renderRow));
      container.dataset.user = uid;
      container.dataset.built = String(Date.now());
    } finally {
      building = false;
    }
  }

  function check() {
    if (!api() || !api().getCurrentUserId()) return;
    const homeTab = document.querySelector('#indexPage:not(.hide) #homeTab');
    if (!homeTab) return;
    const existing = homeTab.querySelector(':scope > .smartHome');
    const stale = !existing
      || existing.dataset.user !== api().getCurrentUserId()
      || Date.now() - Number(existing.dataset.built || 0) > REFRESH_MS;
    if (stale) build(homeTab).catch((e) => console.warn('[SmartHome]', e));
  }

  // ── styles ──

  const css = `
    .smartHome { padding-top: .6em; }
    .sh-row { margin: 0 0 1.4em; }
    .sh-title { display: block; padding-left: 3.3%; margin: 0 0 .35em; text-decoration: none; color: inherit; }
    a.sh-title:hover { text-decoration: underline; }
    .sh-wrap { position: relative; }
    .sh-scroller { display: flex; gap: .55em; overflow-x: auto; padding: .5em 3.3%; scroll-snap-type: x proximity; scrollbar-width: none; }
    .sh-scroller::-webkit-scrollbar { display: none; }
    .sh-card { flex: 0 0 clamp(190px, 21vw, 330px); aspect-ratio: 16 / 9; position: relative; border-radius: .35em; overflow: hidden;
      background: #1c1c1c center / cover no-repeat; scroll-snap-align: start; transition: transform .22s ease, box-shadow .22s ease; outline: none; }
    .sh-card:hover, .sh-card:focus { transform: scale(1.07); z-index: 2; box-shadow: 0 10px 30px rgba(0,0,0,.6); }
    .sh-card span { position: absolute; left: 0; right: 0; bottom: 0; padding: 1.8em .7em .5em; color: #fff; font-weight: 600; font-size: .92em;
      background: linear-gradient(transparent, rgba(0,0,0,.88)); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sh-arrow { position: absolute; top: .5em; bottom: .5em; width: 3.3%; min-width: 34px; z-index: 3; border: 0; cursor: pointer;
      background: rgba(0,0,0,.45); color: #fff; font-size: 2.2em; line-height: 1; opacity: 0; transition: opacity .2s; }
    .sh-prev { left: 0; } .sh-next { right: 0; }
    .sh-wrap:hover .sh-arrow { opacity: 1; }
    .layout-tv .sh-arrow, .layout-mobile .sh-arrow { display: none; }
    @media (prefers-reduced-motion: reduce) { .sh-card { transition: none; } }
  `;

  function start() {
    const style = document.createElement('style');
    style.textContent = css;
    document.head.appendChild(style);
    let pending = null;
    const soon = () => { if (!pending) pending = setTimeout(() => { pending = null; check(); }, 300); };
    new MutationObserver(soon).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
    window.addEventListener('hashchange', check);
    check();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
