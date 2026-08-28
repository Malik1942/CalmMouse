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
    // Start the fade well before the element reaches the viewport, so content
    // is already settling in by the time a fast scroll lands on it.
    { threshold: 0, rootMargin: "0px 0px 25% 0px" }
  );
  revealables.forEach((el) => observer.observe(el));
}

// Downloading is only half the job: the app still needs to be dragged to
// Applications and granted Accessibility. The click is never intercepted —
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
