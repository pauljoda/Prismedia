/** One design concept the user can open and compare on the running app. */
export interface DesignConcept {
  id: string;
  area: "dashboard" | "management";
  title: string;
  /** Short labels naming what the concept contains. */
  features: string[];
  href: string;
}

export const DESIGN_CONCEPTS: readonly DesignConcept[] = [
  {
    id: "dashboard-spectrum",
    area: "dashboard",
    title: "Spectrum home",
    features: [
      "Library prism",
      "Continue by family",
      "Needs you",
    ],
    href: "/concepts/dashboard/spectrum",
  },
  {
    id: "dashboard-beam",
    area: "dashboard",
    title: "Beam & pulse",
    features: [
      "Billboard + pulse column",
      "Family tabs",
      "Latest mosaic",
    ],
    href: "/concepts/dashboard/beam",
  },
  {
    id: "dashboard-canvas",
    area: "dashboard",
    title: "Quiet canvas",
    features: [
      "Light hairline",
      "Large Continue rail",
      "Recent masonry",
    ],
    href: "/concepts/dashboard/canvas",
  },
  {
    id: "manage-control-room",
    area: "management",
    title: "Control room",
    features: [
      "System tiles",
      "Beam header",
      "Library bands",
    ],
    href: "/concepts/manage/control-room",
  },
  {
    id: "manage-settings",
    area: "management",
    title: "Grouped settings",
    features: [
      "Grouped sections",
      "Live status",
      "Filter",
    ],
    href: "/concepts/manage/settings",
  },
  {
    id: "manage-jobs",
    area: "management",
    title: "Job lanes",
    features: [
      "Lanes by job type",
      "24h activity strip",
      "Command bar",
    ],
    href: "/concepts/manage/jobs",
  },
  {
    id: "manage-plugins",
    area: "management",
    title: "Plugin cards",
    features: [
      "Plugin cards",
      "Media bands",
      "Connections inline",
    ],
    href: "/concepts/manage/plugins",
  },
];
