import "./style.css";

// Reveal-on-scroll: elements with .reveal fade in the first time they enter
// the viewport. Motion-averse visitors and browsers without IntersectionObserver
// get the end state immediately, so content is never left invisible.
const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
const revealables = document.querySelectorAll(".reveal");

if (prefersReducedMotion || !("IntersectionObserver" in window)) {
  revealables.forEach((el) => el.classList.add("is-visible"));
} else {
  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        }
      }
    },
    { threshold: 0.15, rootMargin: "0px 0px -40px 0px" }
  );
  revealables.forEach((el) => observer.observe(el));
}

// Downloading is only half the job: the release isn't notarized, so the first
// launch needs the right-click → Open step. The click is never intercepted —
// the href starts the download on its own — we just bring the install steps
// into view behind it, so the instructions are waiting once the file lands.
const installSteps = document.querySelector(".steps");

if (installSteps) {
  document.querySelectorAll('a[href*="/releases/latest/download/"]').forEach((link) => {
    link.addEventListener("click", () => {
      installSteps.scrollIntoView({
        behavior: prefersReducedMotion ? "auto" : "smooth",
        block: "center",
      });
    });
  });
}
