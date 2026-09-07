import type {ComponentPropsWithoutRef} from 'react';

/** Keep native table layout inside a keyboard-accessible scrolling frame. */
export default function DocTable(props: ComponentPropsWithoutRef<'table'>) {
  return (
    <div className="doc-table-scroll" role="region" aria-label="Reference table" tabIndex={0}>
      <table {...props} />
    </div>
  );
}
