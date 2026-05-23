export interface CanvasHeaderBreadcrumb {
  label: string;
  href: string;
  isLast: boolean;
}

export type CanvasHeaderBreadcrumbItem =
  | ({ kind: "crumb" } & CanvasHeaderBreadcrumb)
  | {
      kind: "overflow";
      label: string;
      separatorAfter: false;
      items: CanvasHeaderBreadcrumb[];
    };

export function getCanvasHeaderBreadcrumbItems(
  crumbs: CanvasHeaderBreadcrumb[],
  maxInline = 2,
): CanvasHeaderBreadcrumbItem[] {
  if (crumbs.length <= maxInline) {
    return crumbs.map((crumb) => ({ kind: "crumb", ...crumb }));
  }

  const current = crumbs.at(-1);
  const previousLinks = crumbs.slice(0, -1).filter((crumb) => !crumb.isLast && crumb.href !== "#");

  if (!current || previousLinks.length < 1) {
    return crumbs.map((crumb) => ({ kind: "crumb", ...crumb }));
  }

  return [
    {
      kind: "overflow",
      label: "More breadcrumbs",
      separatorAfter: false,
      items: previousLinks,
    },
    { kind: "crumb", ...current },
  ];
}
