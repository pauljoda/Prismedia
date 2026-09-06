/** Small shared directional icon for marketing links. */
export default function ArrowIcon({down = false, diagonal = false}: {down?: boolean; diagonal?: boolean}) {
  return <svg aria-hidden="true" focusable="false" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" style={{flexShrink: 0, transform: down ? 'rotate(90deg)' : diagonal ? 'rotate(-45deg)' : undefined}}><path d="M4 12h15m-6-6 6 6-6 6" /></svg>;
}
