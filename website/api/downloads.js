// GET /api/downloads: the live total of CalmMouse downloads, for the line under
// the hero.
//
// Adds up GitHub's download_count for every file of every published release,
// so visitors' browsers never call GitHub themselves. Vercel's CDN caches the
// answer for a minute, so GitHub is asked about once a minute rather than once
// per visitor.
//
// It always answers 200. If GitHub can't be reached, or answers with something
// unexpected, the total is null and the page simply leaves its reserved line
// blank.

const RELEASES_URL = "https://api.github.com/repos/Malik1942/CalmMouse/releases?per_page=100";
const MAX_PAGES = 10; // 1,000 releases; needing more means something is wrong
// GitHub's releases list sometimes takes over 5 s when its cache is cold (5.4 s
// measured); 8 s rides that out and still finishes inside a 10 s function limit.
const TIMEOUT_MS = 8000;

export default {
  async fetch() {
    const updatedAt = new Date().toISOString();
    try {
      const total = await countDownloads();
      return Response.json(
        { total, updatedAt },
        { headers: { "Cache-Control": "public, s-maxage=60, stale-while-revalidate=60" } }
      );
    } catch (error) {
      console.warn(`downloads: ${error.message}`); // shows in Vercel's function logs
      // No stale-while-revalidate here, so a GitHub hiccup clears within a minute.
      return Response.json(
        { total: null, updatedAt },
        { headers: { "Cache-Control": "public, s-maxage=60" } }
      );
    }
  },
};

async function countDownloads() {
  const headers = {
    Accept: "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28",
    "User-Agent": "calmmouse-website",
  };
  // Optional locally, required on Vercel, where anonymous calls share a
  // 60-an-hour limit per IP.
  if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;

  const signal = AbortSignal.timeout(TIMEOUT_MS); // one budget for all pages together
  let total = 0;
  let url = RELEASES_URL;
  for (let page = 0; url && page < MAX_PAGES; page++) {
    const response = await fetch(url, { headers, signal });
    if (!response.ok) throw new Error(`GitHub answered ${response.status}`);
    const releases = await response.json();
    if (!Array.isArray(releases)) throw new Error("releases is not a list");
    for (const release of releases) {
      // A token with push access also sees drafts, which the public can't
      // download.
      if (release.draft) continue;
      if (!Array.isArray(release.assets)) throw new Error("a release has no asset list");
      for (const asset of release.assets) {
        if (!Number.isFinite(asset.download_count)) {
          throw new Error("an asset has no download count");
        }
        total += asset.download_count;
      }
    }
    url = nextPage(response.headers.get("link"));
  }
  if (url) throw new Error(`more than ${MAX_PAGES} pages of releases`);
  return total;
}

// GitHub paginates with a Link header:
// <https://api.github.com/...&page=2>; rel="next", <...>; rel="last"
function nextPage(link) {
  return link?.match(/<([^>]+)>;\s*rel="next"/)?.[1] ?? null;
}
