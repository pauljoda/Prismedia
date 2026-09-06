import {useEffect, useId, useRef, type CSSProperties} from 'react';
import ArrowIcon from './ArrowIcon';
import {MEDIA_FAMILIES} from './media-families';
import styles from './PrismStory.module.css';

const clamp = (value: number) => Math.max(0, Math.min(1, value));
const phase = (progress: number, start: number, end: number) => clamp((progress - start) / (end - start));

/**
 * Progressively reveals the optical story from ordinary document scroll.
 * The server-rendered and reduced-motion versions show the complete illustration.
 */
export default function PrismStory() {
  const sectionRef = useRef<HTMLElement>(null);
  const id = useId().replace(/:/g, '');

  useEffect(() => {
    const section = sectionRef.current;
    if (!section) return;
    const motion = window.matchMedia('(min-width: 761px) and (min-height: 640px) and (prefers-reduced-motion: no-preference)');
    let frame = 0;
    let observing = false;

    function draw() {
      frame = 0;
      if (!section || !motion.matches) return;
      const rect = section.getBoundingClientRect();
      const progress = clamp((60 - rect.top) / Math.max(1, rect.height - window.innerHeight + 60));
      section.style.setProperty('--arrival', String(phase(progress, 0, 0.25)));
      section.style.setProperty('--refraction', String(phase(progress, 0.22, 0.42)));
      section.style.setProperty('--dispersion', String(phase(progress, 0.34, 0.82)));
      // A brief light bloom at refraction, settling as the spectrum becomes readable.
      section.style.setProperty('--bloom', String(Math.sin(phase(progress, 0.2, 0.72) * Math.PI)));
    }

    function schedule() {
      if (!frame) frame = window.requestAnimationFrame(draw);
    }

    function configure() {
      if (!section) return;
      section.dataset.motion = String(motion.matches);
      if (motion.matches && !observing) {
        window.addEventListener('scroll', schedule, {passive: true});
        observing = true;
      } else if (!motion.matches && observing) {
        window.removeEventListener('scroll', schedule);
        observing = false;
      }
      schedule();
    }

    configure();
    motion.addEventListener('change', configure);
    window.addEventListener('resize', schedule);
    // Artwork above the section can change its document position as it loads.
    const resizeObserver = new ResizeObserver(schedule);
    resizeObserver.observe(document.body);
    return () => {
      window.cancelAnimationFrame(frame);
      motion.removeEventListener('change', configure);
      window.removeEventListener('scroll', schedule);
      window.removeEventListener('resize', schedule);
      resizeObserver.disconnect();
    };
  }, []);

  return (
    <section ref={sectionRef} className={styles.story} id="product" aria-labelledby="prism-title">
      <div className={styles.sticky}>
        <div className={styles.heading}>
          <h2 id="prism-title">Different media. A shared foundation.</h2>
          <p>Finding it. Identifying it. Bringing it home.<br />A familiar journey, whatever you enjoy.</p>
        </div>
        <div className={styles.illustration}>
          <svg viewBox="0 0 1200 540" aria-hidden="true" focusable="false" className={styles.optics}>
            <defs>
              <linearGradient id={`${id}-face`} x1="0" y1="0" x2="1" y2="1">
                <stop offset="0" stopColor="#fafbff" stopOpacity="0.24" />
                <stop offset="0.5" stopColor="#c5c9d3" stopOpacity="0.025" />
                <stop offset="1" stopColor="#f0f3ff" stopOpacity="0.12" />
              </linearGradient>
              <linearGradient id={`${id}-edge`} x1="0" y1="0" x2="1" y2="1">
                <stop stopColor="#f5f6fa" stopOpacity="0.85" />
                <stop offset="0.55" stopColor="#d9dce5" stopOpacity="0.15" />
                <stop offset="1" stopColor="#f5f6fa" stopOpacity="0.6" />
              </linearGradient>
              <radialGradient id={`${id}-ground`}>
                <stop stopColor="#bfc6df" stopOpacity="0.1" />
                <stop offset="1" stopColor="#bfc6df" stopOpacity="0" />
              </radialGradient>
              <filter id={`${id}-glow`} x="-50%" y="-100%" width="200%" height="300%">
                <feGaussianBlur stdDeviation="4" />
              </filter>
              {MEDIA_FAMILIES.map((family, index) => (
                <linearGradient key={family.kind} id={`${id}-ray-${index}`} style={family.style}>
                  <stop stopColor="var(--beam-color)" />
                  <stop offset="1" stopColor="var(--beam-secondary)" />
                </linearGradient>
              ))}
            </defs>
            <ellipse cx="560" cy="422" rx="260" ry="66" fill={`url(#${id}-ground)`} />
            <path className={styles.guide} d="M60 270H500M616 270H980" />
            <g className={styles.incoming}>
              <path className={styles.beamGlow} d="M60 270H500" pathLength="1" filter={`url(#${id}-glow)`} />
              <path className={styles.whiteBeam} d="M60 270H500" pathLength="1" />
            </g>
            <g className={styles.prism}>
              <path d="M440 398L565 140L705 398Z" fill={`url(#${id}-face)`} stroke={`url(#${id}-edge)`} strokeWidth="1.4" />
              <path d="M565 140L585 365L705 398Z" fill="#dce2f1" fillOpacity="0.07" stroke="#e9efff" strokeOpacity="0.13" />
              <path d="M440 398L585 365L705 398" fill="none" stroke="#e9efff" strokeOpacity="0.24" />
              <path d="M565 140L585 365L440 398" fill="none" stroke="#e9efff" strokeOpacity="0.14" />
              <path className={styles.insideBeam} d="M502 270L578 280L635 269" pathLength="1" />
            </g>
            <g className={styles.rays}>
              {MEDIA_FAMILIES.map((family, index) => {
                const y = 79 + index * 51;
                const path = `M635 269 L968 ${y}`;
                return (
                  <g key={family.kind} className={styles.ray} style={{...family.style, '--ray-index': index} as CSSProperties}>
                    <path className={styles.rayGlow} d={path} pathLength="1" stroke={`url(#${id}-ray-${index})`} filter={`url(#${id}-glow)`} />
                    <path className={styles.rayLine} d={path} pathLength="1" stroke={`url(#${id}-ray-${index})`} />
                    <circle className={styles.endpoint} cx="974" cy={y} r="2.5" fill="var(--beam-color)" />
                    <text className={styles.rayLabel} x="994" y={y + 5}>{family.label}</text>
                  </g>
                );
              })}
            </g>
            <circle className={styles.flash} cx="635" cy="269" r="10" fill="#fff" filter={`url(#${id}-glow)`} />
            <g className={styles.inputLabel}><text x="60" y="225">ONE COLLECTION</text><text x="60" y="305">Identity · files · history</text></g>
            <text className={styles.prismLabel} x="568" y="446" textAnchor="middle">PRISMEDIA</text>
          </svg>
          <div className={styles.mobileLegend} aria-hidden="true">
            {MEDIA_FAMILIES.map((family) => <span key={family.kind} style={family.style}><i />{family.label}</span>)}
          </div>
        </div>
        <div className={styles.footnote}>
          <p>One place to manage the collection.<br /><strong>Thoughtful ways to watch, listen, read, and browse.</strong></p>
          <a href="#experiences" className={styles.skipStory}>Explore the experiences <ArrowIcon down /></a>
        </div>
      </div>
      <ul className={styles.accessibleLegend}>
        {MEDIA_FAMILIES.map((family) => <li key={family.kind}>{family.label}: {family.detail}</li>)}
      </ul>
    </section>
  );
}
