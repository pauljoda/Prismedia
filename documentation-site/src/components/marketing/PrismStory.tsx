import {useEffect, useId, useRef, type CSSProperties} from 'react';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
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
  const logoUrl = useBaseUrl('/img/logo-mark.png');
  const homeUrl = useBaseUrl('/');

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
      section.style.setProperty('--arrival', String(phase(progress, 0, 0.3)));
      section.style.setProperty('--dispersion', String(phase(progress, 0.38, 0.86)));
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
          <svg viewBox="0 0 1200 560" className={styles.optics} aria-label="Light passes through the Prismedia logo into the different media experiences">
            <defs>
              <linearGradient id={`${id}-white`} x1="30" y1="0" x2="562" y2="0" gradientUnits="userSpaceOnUse">
                <stop stopColor="#dbeaff" stopOpacity="0" />
                <stop offset="0.26" stopColor="#e6edff" stopOpacity="0.7" />
                <stop offset="1" stopColor="#fff" />
              </linearGradient>
              <radialGradient id={`${id}-floor`}>
                <stop stopColor="#9eacdc" stopOpacity="0.2" />
                <stop offset="0.45" stopColor="#676cc5" stopOpacity="0.07" />
                <stop offset="1" stopColor="#676cc5" stopOpacity="0" />
              </radialGradient>
              <radialGradient id={`${id}-impact`}>
                <stop stopColor="#fff" />
                <stop offset="0.14" stopColor="#e9ecff" stopOpacity="0.8" />
                <stop offset="0.5" stopColor="#b8cbff" stopOpacity="0.15" />
                <stop offset="1" stopColor="#b8cbff" stopOpacity="0" />
              </radialGradient>
              <linearGradient id={`${id}-reflection`} x1="0" y1="416" x2="0" y2="520" gradientUnits="userSpaceOnUse">
                <stop stopColor="#fff" stopOpacity="0.2" />
                <stop offset="1" stopColor="#fff" stopOpacity="0" />
              </linearGradient>
              <mask id={`${id}-reflection-mask`}>
                <rect x="380" y="418" width="364" height="104" fill={`url(#${id}-reflection)`} />
              </mask>
              <filter id={`${id}-glow`} x="-20%" y="-100%" width="140%" height="300%" colorInterpolationFilters="sRGB">
                <feGaussianBlur stdDeviation="5" />
              </filter>
              <filter id={`${id}-soft-reflection`}><feGaussianBlur stdDeviation="1.5" /></filter>
            </defs>
            <g aria-hidden="true">
              <ellipse cx="562" cy="428" rx="310" ry="95" fill={`url(#${id}-floor)`} />
              <g mask={`url(#${id}-reflection-mask)`}>
                <image href={logoUrl} x="382" y="108" width="360" height="331.875" transform="translate(0 835) scale(1 -1)" filter={`url(#${id}-soft-reflection)`} />
              </g>
            </g>
            {MEDIA_FAMILIES.map((family, index) => {
              const y = 104 + index * 48;
              const path = `M562 304 C710 304 752 ${y} 942 ${y}`;
              return (
                <g key={family.kind} className={styles.ray} style={{...family.style, '--ray-index': index} as CSSProperties}>
                  <g aria-hidden="true" className={styles.lightPath}>
                    <path className={styles.rayGlow} d={path} pathLength="1" filter={`url(#${id}-glow)`} />
                    <path className={styles.rayLine} data-spectrum-line d={path} pathLength="1" />
                  </g>
                  <a href={`${homeUrl}${family.href.slice(1)}`} className={styles.mediaLink} aria-label={`${family.label}: ${family.detail}`}>
                    <rect x="952" y={y - 20} width="218" height="43" rx="8" />
                    <text x="967" y={y - 1} className={styles.rayLabel}>{family.label}</text>
                    <text x="967" y={y + 15} className={styles.rayDetail}>{family.detail}</text>
                    <path className={styles.linkArrow} d={`M1141 ${y}h13m-5-5 5 5-5 5`} />
                  </a>
                </g>
              );
            })}
            <g aria-hidden="true" className={styles.prism}>
              <image href={logoUrl} x="382" y="108" width="360" height="331.875" className={styles.logo} />
              {/* One continuous path touches the actual artwork's left facet and central vertex. */}
              <path className={styles.beamGlow} d="M30 326L466 304L562 304" pathLength="1" stroke={`url(#${id}-white)`} filter={`url(#${id}-glow)`} />
              <path className={styles.whiteBeam} data-white-light d="M30 326L466 304L562 304" pathLength="1" stroke={`url(#${id}-white)`} />
              <circle className={styles.flash} cx="562" cy="304" r="36" fill={`url(#${id}-impact)`} />
              <text className={styles.inputLabel} x="74" y="287">Your collection</text>
              <text className={styles.inputDetail} x="74" y="307">Identity · files · history</text>
            </g>
          </svg>
          <div className={styles.mobileLegend}>
            {MEDIA_FAMILIES.map((family) => (
              <Link key={family.kind} to={family.href} style={family.style}>
                <i aria-hidden="true" />{family.label}<ArrowIcon />
              </Link>
            ))}
          </div>
        </div>
        <div className={styles.footnote}>
          <p>One place to manage the collection.<br /><strong>Thoughtful ways to watch, listen, read, and browse.</strong></p>
          <a href="#experiences" className={`marketing-glass ${styles.skipStory}`}>Explore the experiences <ArrowIcon down /></a>
        </div>
      </div>
    </section>
  );
}
