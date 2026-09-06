import {useEffect, useRef} from 'react';
import {MEDIA_FAMILIES} from './media-families';
import styles from './AmbientLight.module.css';

// Evenly spaced around the perimeter; the center stays clear for content.
const LIGHT_POSITIONS = [[0, 0], [50, 0], [100, 0], [100, 50], [100, 100], [50, 100], [0, 100], [0, 50]] as const;

/** The app's spectrum atmosphere, with slow drift, pointer response, and system motion preferences. */
export default function AmbientLight() {
  const fieldRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const field = fieldRef.current;
    const page = field?.closest('main');
    if (!field || !page) return;
    const preference = matchMedia('(prefers-reduced-motion: no-preference)');
    const pointer = matchMedia('(pointer: fine)');
    let visible = true;
    let frame = 0;
    let x = 0;
    let y = 0;

    function synchronize() {
      if (!field) return;
      field.dataset.active = String(visible && !document.hidden && preference.matches);
      if (field.dataset.active === 'false') {
        field.style.removeProperty('--pointer-x');
        field.style.removeProperty('--pointer-y');
      }
    }

    function move(event: PointerEvent) {
      if (!field || field.dataset.active !== 'true' || !pointer.matches) return;
      x = (event.clientX / innerWidth - 0.5) * 32;
      y = (event.clientY / innerHeight - 0.5) * 24;
      if (frame) return;
      frame = requestAnimationFrame(() => {
        frame = 0;
        if (field.dataset.active !== 'true') return;
        field.style.setProperty('--pointer-x', `${x}px`);
        field.style.setProperty('--pointer-y', `${y}px`);
      });
    }

    const observer = new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      synchronize();
    });
    observer.observe(page);
    preference.addEventListener('change', synchronize);
    document.addEventListener('visibilitychange', synchronize);
    window.addEventListener('pointermove', move, {passive: true});
    synchronize();
    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      preference.removeEventListener('change', synchronize);
      document.removeEventListener('visibilitychange', synchronize);
      window.removeEventListener('pointermove', move);
    };
  }, []);

  return <>
    <div ref={fieldRef} className={styles.field} data-atmosphere data-active="false" aria-hidden="true">
      <div className={styles.drift}>
        {[0, 4].map((start) => <div className={styles.orbit} key={start}>
          {MEDIA_FAMILIES.slice(start, start + 4).map((family, offset) => {
            const [left, top] = LIGHT_POSITIONS[start + offset];
            return <span key={family.kind} className={styles.light} style={{...family.style, left: `${left}%`, top: `${top}%`}} />;
          })}
        </div>)}
      </div>
    </div>
  </>;
}
