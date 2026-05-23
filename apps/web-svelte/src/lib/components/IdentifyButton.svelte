<script lang="ts">
  import { onMount } from "svelte";
  import {
    AlertCircle,
    Check,
    ChevronDown,
    ChevronLeft,
    ChevronRight,
    Eye,
    Image as ImageIcon,
    Loader2,
    ScanSearch,
    Sparkles,
    X,
    Zap,
  } from "@lucide/svelte";
  import { portal } from "$lib/actions/portal";
  import {
    buildProposalForApply,
    findRelationshipImage,
    groupProposalRows,
    isNewRelationshipTitle,
    relationshipProposals,
    reviewChildProposals,
    relationshipTitlesFromEntityThumbnails,
    structuralChildProposals,
    type IdentifyProposalRow,
    type IdentifyRelationshipTitles,
  } from "$lib/components/identify-review";
  import {
    applyIdentifyProposal,
    fetchIdentifyEntity,
    fetchIdentifyProviders,
    identifyEntity,
    type CreditPatch,
    type EntityMetadataProposal,
    type EntitySearchCandidate,
    type ImageCandidate,
    type PluginProvider,
  } from "$lib/api/identify";
  import { fetchEntityThumbnails, type EntityDetailCard } from "$lib/api/prismedia";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  type IdentifyEntityKind = "video_series" | "video_movie" | "video_episode" | "book";

  interface IdentifyTarget {
    entityKind: IdentifyEntityKind | string;
    entityId: string;
    title: string;
    existingCreditNames?: string[];
    existingTags?: string[];
  }

  interface Props {
    entityKind: IdentifyEntityKind | string;
    entityId: string;
    title: string;
    label?: string;
    class?: string;
    entities?: IdentifyTarget[];
    existingCreditNames?: string[];
    existingTags?: string[];
    onApplied?: () => void | Promise<void>;
  }

  let {
    entityKind,
    entityId,
    title,
    label = "Identify",
    class: className,
    entities,
    existingCreditNames = [],
    existingTags = [],
    onApplied,
  }: Props = $props();

  const fieldLabels: Record<string, string> = {
    title: "Title",
    description: "Description",
    externalIds: "Provider IDs",
    urls: "Links",
    tags: "Tags",
    studio: "Studio",
    credits: "Credits",
    dates: "Dates",
    stats: "Stats",
    positions: "Positions",
    classification: "Classification",
    images: "Artwork",
  };
  const fieldKeys = Object.keys(fieldLabels);

  let workflowOpen = $state(false);
  let providers = $state<PluginProvider[]>([]);
  let loadingProviders = $state(false);
  let providerKindLoaded = $state<string | null>(null);
  let identifying = $state(false);
  let proposal = $state<EntityMetadataProposal | null>(null);
  let reviewPath = $state<string[]>([]);
  let activeIndex = $state(0);
  let selectedProviderId = $state("");
  let selectedFieldsByProposal = $state<Record<string, Record<string, boolean>>>({});
  let selectedImagesByProposal = $state<Record<string, Record<string, string | null>>>({});
  let selectedCreditsByProposal = $state<Record<string, Record<string, boolean>>>({});
  let selectedTagsByProposal = $state<Record<string, Record<string, boolean>>>({});
  let selectedCascade = $state<Record<string, boolean>>({});
  let entitiesById = $state<Record<string, EntityDetailCard>>({});
  let relationshipTitlesByEntityId = $state<Record<string, IdentifyRelationshipTitles>>({});
  let loadingViewEntity = $state(false);
  let applying = $state(false);
  let error = $state<string | null>(null);
  let expandedSections = $state<Record<string, boolean>>({ fields: true, tags: true, credits: true, studio: true, seasons: true, related: true, artwork: true, candidates: true });
  let lightboxGroup = $state<string | null>(null);
  let modalBodyElement = $state<HTMLDivElement | null>(null);

  const scalarFieldKeys = ["title", "description", "externalIds", "urls", "dates", "stats", "positions", "classification"];
  const activeProposal = $derived.by(() => {
    if (!proposal) return null;
    const activeId = reviewPath.at(-1) ?? proposal.proposalId;
    return findProposal(proposal, activeId) ?? proposal;
  });
  const selectedFields = $derived(activeProposal ? selectedFieldsByProposal[activeProposal.proposalId] ?? {} : {});
  const selectedImages = $derived(activeProposal ? selectedImagesByProposal[activeProposal.proposalId] ?? {} : {});
  const selectedCredits = $derived(activeProposal ? selectedCreditsByProposal[activeProposal.proposalId] ?? {} : {});
  const selectedTags = $derived(activeProposal ? selectedTagsByProposal[activeProposal.proposalId] ?? {} : {});

  function toggleSection(section: string) {
    expandedSections = { ...expandedSections, [section]: !expandedSections[section] };
  }

  function openLightbox(kind: string) {
    lightboxGroup = kind;
  }

  function closeLightbox() {
    lightboxGroup = null;
  }

  const reviewableImageGroups = $derived.by(() => {
    if (!activeProposal) return [];
    return imageGroups(activeProposal.images).filter((g) => imageAspect(g.kind) !== "logo");
  });

  const lightboxImages = $derived.by(() => {
    if (!lightboxGroup || !proposal) return [];
    const group = reviewableImageGroups.find((g) => g.kind === lightboxGroup);
    return group?.images ?? [];
  });

  const lightboxSelectedUrl = $derived(lightboxGroup ? selectedImages[lightboxGroup] ?? null : null);

  const targets = $derived.by((): IdentifyTarget[] => {
    if (entities?.length) return entities;
    return [{ entityKind, entityId, title, existingCreditNames, existingTags }];
  });
  const activeTarget = $derived(targets[Math.min(activeIndex, Math.max(0, targets.length - 1))]);
  const providerKind = $derived(mapKind(activeTarget?.entityKind ?? entityKind));
  const activeReviewEntityId = $derived(activeProposal?.targetEntityId ?? activeTarget?.entityId ?? entityId);
  const activeReviewEntity = $derived(activeReviewEntityId ? entitiesById[activeReviewEntityId] ?? null : null);
  const reviewKind = $derived(mapKind(activeReviewEntity?.kind ?? activeProposal?.targetKind ?? providerKind));
  const reviewTitle = $derived(activeProposal?.patch.title ?? activeReviewEntity?.title ?? activeProposal?.targetKind ?? title);
  const installedProviders = $derived(
    providers.filter((provider) => provider.installed && provider.enabled),
  );
  const runnableProviders = $derived(
    installedProviders.filter((provider) => provider.missingAuthKeys.length === 0),
  );
  const selectedProvider = $derived.by(() =>
    installedProviders.find((provider) => provider.id === selectedProviderId) ?? null,
  );
  const hasTargets = $derived(targets.length > 0);

  onMount(() => {
    void loadProviders(providerKind);
  });

  $effect(() => {
    if (!workflowOpen || typeof document === "undefined") return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = previousOverflow;
    };
  });

  $effect(() => {
    if (!workflowOpen) return;
    void loadProviders(reviewKind);
  });

  async function loadProviders(kind = providerKind) {
    if (loadingProviders || providerKindLoaded === kind) return;
    loadingProviders = true;
    error = null;
    try {
      providers = await fetchIdentifyProviders(kind);
      providerKindLoaded = kind;
    } catch (err) {
      error = readError(err);
    } finally {
      loadingProviders = false;
    }
  }

  async function openWorkflow() {
    workflowOpen = true;
    proposal = null;
    reviewPath = [];
    selectedProviderId = "";
    resetReviewSelections();
    selectedCascade = {};
    error = null;
    await ensureEntityLoaded(activeTarget?.entityId ?? entityId);
    await loadProviders(providerKind);
  }

  function closeWorkflow() {
    workflowOpen = false;
    proposal = null;
    reviewPath = [];
    selectedProviderId = "";
    resetReviewSelections();
    selectedCascade = {};
    error = null;
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === "Escape" && workflowOpen) closeWorkflow();
  }

  async function selectProvider(providerId: string) {
    selectedProviderId = providerId;
    proposal = null;
    reviewPath = [];
    resetReviewSelections();
    error = null;
    const provider = runnableProviders.find((row) => row.id === providerId);
    if (provider) await run(provider);
  }

  async function run(provider: PluginProvider, candidate?: EntitySearchCandidate) {
    if (!activeTarget) return;
    selectedProviderId = provider.id;
    identifying = true;
    error = null;
    try {
      const nextProposal = await identifyEntity(activeTarget.entityId, provider.id, candidate
        ? { externalIds: candidate.externalIds }
        : undefined);
      proposal = nextProposal;
      reviewPath = [];
      initializeReviewSelections(nextProposal);
      selectedCascade = defaultCascadeSelection(nextProposal);
    } catch (err) {
      error = readError(err);
    } finally {
      identifying = false;
    }
  }

  function rerunCandidate(candidate: EntitySearchCandidate) {
    if (!selectedProvider) return;
    void rerunActiveCandidate(selectedProvider, candidate);
  }

  async function rerunActiveCandidate(provider: PluginProvider, candidate: EntitySearchCandidate) {
    if (!activeReviewEntityId || !proposal || !activeProposal) return;
    identifying = true;
    error = null;
    try {
      const nextProposal = await identifyEntity(activeReviewEntityId, provider.id, { externalIds: candidate.externalIds });
      const isRoot = activeProposal.proposalId === proposal.proposalId;
      const nextRoot = isRoot
        ? nextProposal
        : replaceProposal(proposal, activeProposal.proposalId, nextProposal);
      proposal = nextRoot;
      reviewPath = isRoot
        ? []
        : [...reviewPath.slice(0, -1), nextProposal.proposalId];
      initializeReviewSelections(nextRoot);
      selectedCascade = defaultCascadeSelection(nextRoot);
      if (nextProposal.targetEntityId) void ensureEntityLoaded(nextProposal.targetEntityId);
    } catch (err) {
      error = readError(err);
    } finally {
      identifying = false;
    }
  }

  function toggleField(field: string) {
    if (!activeProposal) return;
    selectedFieldsByProposal = {
      ...selectedFieldsByProposal,
      [activeProposal.proposalId]: { ...selectedFields, [field]: !selectedFields[field] },
    };
  }

  function toggleCredit(key: string) {
    if (!activeProposal) return;
    selectedCreditsByProposal = {
      ...selectedCreditsByProposal,
      [activeProposal.proposalId]: { ...selectedCredits, [key]: !selectedCredits[key] },
    };
  }

  function toggleTag(tag: string) {
    if (!activeProposal) return;
    selectedTagsByProposal = {
      ...selectedTagsByProposal,
      [activeProposal.proposalId]: { ...selectedTags, [tag]: !selectedTags[tag] },
    };
  }

  function setCascadeNodeSelected(node: CascadeNode, selected: boolean) {
    const next = { ...selectedCascade };
    for (const id of cascadeNodeIds(node)) next[id] = selected;
    selectedCascade = next;
  }

  function isNewTag(tag: string): boolean {
    const hydrated = activeReviewEntity ? tagTitlesFromEntity(activeReviewEntity) : [];
    const existing = hydrated.length > 0 ? hydrated : (activeTarget?.existingTags ?? []);
    return isNewRelationshipTitle(tag, existing);
  }

  const selectedTagCount = $derived(Object.values(selectedTags).filter(Boolean).length);

  function creditToCard(credit: CreditPatch, result: EntityMetadataProposal): EntityThumbnailCard {
    const imageUrl = findRelationshipImage(result, "person", credit.name);
    return {
      entity: {
        id: `proposal-${credit.name}`,
        kind: "person",
        title: credit.name,
        parentEntityId: null,
      sortOrder: null,
      relationships: [],
        capabilities: [],
        childrenByKind: [],
      },
      aspectRatio: { width: 4, height: 5 },
      cover: imageUrl ? { src: imageUrl, alt: credit.name } : null,
      hover: { kind: "none" },
      subtitle: creditSubtitle(credit),
    };
  }

  const creditCards = $derived.by((): EntityThumbnailCard[] => {
    if (!activeProposal) return [];
    return activeProposal.patch.credits.map((c) => creditToCard(c, activeProposal));
  });

  interface CascadeNode {
    proposalId: string;
    kind: string;
    title: string;
    description: string | null;
    date: string | null;
    positionLabel: string;
    imageUrl: string | null;
    creditCount: number;
    metadataCount: number;
    children: CascadeNode[];
    targetEntityId: string | null;
  }

  interface CascadeNodeRow {
    id: string;
    label: string;
    nodes: CascadeNode[];
  }

  const childCascadeRows = $derived.by((): CascadeNodeRow[] => {
    if (!activeProposal) return [];
    return proposalRowsToCascadeRows(groupProposalRows(structuralChildProposals(activeProposal)));
  });

  const relatedCascadeRows = $derived.by((): CascadeNodeRow[] => {
    if (!activeProposal) return [];
    return proposalRowsToCascadeRows(groupProposalRows(relationshipProposals(activeProposal)));
  });
  const hasRelatedPersonProposals = $derived(activeProposal ? relationshipProposals(activeProposal).some((child) => child.targetKind === "person") : false);
  const hasRelatedStudioProposals = $derived(activeProposal ? relationshipProposals(activeProposal).some((child) => child.targetKind === "studio") : false);

  const childCascadeTotalCount = $derived(childCascadeRows.reduce((sum, row) => sum + row.nodes.reduce((rowSum, node) => rowSum + cascadeNodeCount(node), 0), 0));
  const childCascadeSelectedCount = $derived(childCascadeRows.reduce((sum, row) => sum + row.nodes.reduce((rowSum, node) => rowSum + selectedCascadeNodeCount(node), 0), 0));
  const relatedCascadeTotalCount = $derived(relatedCascadeRows.reduce((sum, row) => sum + row.nodes.reduce((rowSum, node) => rowSum + cascadeNodeCount(node), 0), 0));
  const relatedCascadeSelectedCount = $derived(relatedCascadeRows.reduce((sum, row) => sum + row.nodes.reduce((rowSum, node) => rowSum + selectedCascadeNodeCount(node), 0), 0));

  const studioCard = $derived.by((): EntityThumbnailCard | null => {
    if (!activeProposal?.patch.studio) return null;
    const imageUrl = findRelationshipImage(activeProposal, "studio", activeProposal.patch.studio);
    return {
      entity: {
        id: `proposal-studio`,
        kind: "studio",
        title: activeProposal.patch.studio,
        parentEntityId: null,
      sortOrder: null,
      relationships: [],
        capabilities: [],
        childrenByKind: [],
      },
      aspectRatio: "wide",
      cover: imageUrl ? { src: imageUrl, alt: activeProposal.patch.studio } : null,
      hover: { kind: "none" },
    };
  });

  async function apply(closeAfter = true) {
    if (!proposal || !activeTarget) return;
    applying = true;
    error = null;
    try {
      const rootFields = selectedFieldsByProposal[proposal.proposalId] ?? {};
      const selectedRootFields = Object.entries(rootFields)
        .filter(([, enabled]) => enabled)
        .map(([field]) => field);
      await applyIdentifyProposal(activeTarget.entityId, proposalForApply(proposal), selectedRootFields, selectedImagesByProposal[proposal.proposalId] ?? {});
      await onApplied?.();
      if (closeAfter || activeIndex >= targets.length - 1) closeWorkflow();
      else moveTo(activeIndex + 1);
    } catch (err) {
      error = readError(err);
    } finally {
      applying = false;
    }
  }

  function moveTo(index: number) {
    if (index < 0 || index >= targets.length) return;
    activeIndex = index;
    proposal = null;
    reviewPath = [];
    resetReviewSelections();
    selectedCascade = {};
    error = null;
    if (selectedProvider) void run(selectedProvider);
  }

  function proposalForApply(result: EntityMetadataProposal): EntityMetadataProposal {
    return buildProposalForApply(result, {
      selectedFieldsByProposal,
      selectedImagesByProposal,
      selectedCreditsByProposal,
      selectedTagsByProposal,
      selectedCascade,
    });
  }

  async function ensureEntityLoaded(id: string) {
    if (!id || entitiesById[id]) return;
    loadingViewEntity = true;
    try {
      const entity = await fetchIdentifyEntity(id);
      entitiesById = { ...entitiesById, [id]: entity };
      await hydrateRelationshipTitles(entity);
    } catch {
      // Keep the review usable from proposal data if the live card cannot load.
    } finally {
      loadingViewEntity = false;
    }
  }

  async function hydrateRelationshipTitles(entity: EntityDetailCard) {
    if (relationshipTitlesByEntityId[entity.id]) return;
    const ids = entity.relationships.flatMap((group) => group.entities.map((relationship) => relationship.id));
    if (ids.length === 0) {
      relationshipTitlesByEntityId = {
        ...relationshipTitlesByEntityId,
        [entity.id]: { tags: [], credits: [] },
      };
      return;
    }

    try {
      const thumbnails = await fetchEntityThumbnails(ids);
      relationshipTitlesByEntityId = {
        ...relationshipTitlesByEntityId,
        [entity.id]: relationshipTitlesFromEntityThumbnails(entity, thumbnails),
      };
    } catch {
      relationshipTitlesByEntityId = {
        ...relationshipTitlesByEntityId,
        [entity.id]: { tags: [], credits: [] },
      };
    }
  }

  function fieldValue(result: EntityMetadataProposal, field: string): string {
    const patch = result.patch;
    if (field === "title") return patch.title ?? "";
    if (field === "description") return patch.description ?? "";
    if (field === "externalIds") return entries(patch.externalIds).join(", ");
    if (field === "urls") return patch.urls.join(", ");
    if (field === "tags") return patch.tags.join(", ");
    if (field === "studio") return patch.studio ?? "";
    if (field === "credits") return patch.credits.length > 0 ? `${patch.credits.length} credit${patch.credits.length === 1 ? "" : "s"}` : "";
    if (field === "dates") return entries(patch.dates).join(", ");
    if (field === "stats") return entries(patch.stats).join(", ");
    if (field === "positions") return entries(patch.positions).join(", ");
    if (field === "classification") return patch.classification ?? "";
    if (field === "images") return result.images.length > 0 ? `${result.images.length} candidate${result.images.length === 1 ? "" : "s"}` : "";
    return "";
  }

  function hasField(result: EntityMetadataProposal, field: string): boolean {
    return fieldValue(result, field).trim().length > 0;
  }

  function imageGroups(images: ImageCandidate[]): Array<{ kind: string; images: ImageCandidate[] }> {
    const groups: Record<string, ImageCandidate[]> = {};
    for (const image of images) groups[image.kind] = [...(groups[image.kind] ?? []), image];
    return Object.entries(groups).map(([kind, rows]) => ({ kind, images: rows }));
  }

  function findProposal(root: EntityMetadataProposal, proposalId: string): EntityMetadataProposal | null {
    if (root.proposalId === proposalId) return root;
    for (const child of reviewChildProposals(root)) {
      const found = findProposal(child, proposalId);
      if (found) return found;
    }
    return null;
  }

  function findParentProposalId(root: EntityMetadataProposal, proposalId: string): string | null {
    for (const child of reviewChildProposals(root)) {
      if (child.proposalId === proposalId) return root.proposalId;
      const nested = findParentProposalId(child, proposalId);
      if (nested) return nested;
    }
    return null;
  }

  function replaceProposal(
    root: EntityMetadataProposal,
    proposalId: string,
    replacement: EntityMetadataProposal,
  ): EntityMetadataProposal {
    if (root.proposalId === proposalId) return replacement;
    return {
      ...root,
      children: root.children.map((child) => replaceProposal(child, proposalId, replacement)),
      relationships: root.relationships.map((child) => replaceProposal(child, proposalId, replacement)),
    };
  }

  function enterReviewScope(node: CascadeNode) {
    if (!proposal) return;
    const parentId = findParentProposalId(proposal, node.proposalId);
    if (!parentId && node.proposalId !== proposal.proposalId) return;
    reviewPath = [...reviewPath.filter((id) => id !== node.proposalId), node.proposalId];
    lightboxGroup = null;
    if (node.targetEntityId) void ensureEntityLoaded(node.targetEntityId);
    queueMicrotask(scrollReviewToTop);
  }

  function leaveReviewScope() {
    reviewPath = reviewPath.slice(0, -1);
    lightboxGroup = null;
    queueMicrotask(scrollReviewToTop);
  }

  function scrollReviewToTop() {
    modalBodyElement?.scrollTo({ top: 0, behavior: "auto" });
  }

  function resetReviewSelections() {
    selectedFieldsByProposal = {};
    selectedImagesByProposal = {};
    selectedCreditsByProposal = {};
    selectedTagsByProposal = {};
  }

  function initializeReviewSelections(root: EntityMetadataProposal) {
    const fields: Record<string, Record<string, boolean>> = {};
    const images: Record<string, Record<string, string | null>> = {};
    const credits: Record<string, Record<string, boolean>> = {};
    const tags: Record<string, Record<string, boolean>> = {};

    visitProposal(root, (item) => {
      fields[item.proposalId] = Object.fromEntries(fieldKeys.map((field) => [field, hasField(item, field)]));
      images[item.proposalId] = defaultImageSelection(item.images);
      credits[item.proposalId] = Object.fromEntries(
        item.patch.credits.map((credit, index) => [creditKey(credit, index), true]),
      );
      tags[item.proposalId] = Object.fromEntries(item.patch.tags.map((tag) => [tag, true]));
    });

    selectedFieldsByProposal = fields;
    selectedImagesByProposal = images;
    selectedCreditsByProposal = credits;
    selectedTagsByProposal = tags;
  }

  function visitProposal(root: EntityMetadataProposal, visit: (proposal: EntityMetadataProposal) => void) {
    visit(root);
    for (const child of reviewChildProposals(root)) visitProposal(child, visit);
  }

  function setImageSelection(kind: string, url: string | null) {
    if (!activeProposal) return;
    selectedImagesByProposal = {
      ...selectedImagesByProposal,
      [activeProposal.proposalId]: { ...selectedImages, [kind]: url },
    };
  }

  function toggleImageSelection(group: { kind: string; images: ImageCandidate[] }) {
    const current = selectedImages[group.kind];
    setImageSelection(group.kind, current ? null : group.images[0]?.url ?? null);
  }

  function relationshipChildren(result: EntityMetadataProposal): EntityMetadataProposal[] {
    return reviewChildProposals(result);
  }

  function proposalRowsToCascadeRows(rows: IdentifyProposalRow[]): CascadeNodeRow[] {
    return rows.map((row) => ({
      id: row.id,
      label: row.label,
      nodes: row.proposals.map(toCascadeNode).sort(compareCascadeNodes),
    }));
  }

  function toCascadeNode(child: EntityMetadataProposal): CascadeNode {
    const nested = relationshipChildren(child).map(toCascadeNode).sort(compareCascadeNodes);
    return {
      proposalId: child.proposalId,
      kind: child.targetKind,
      title: child.patch.title ?? fallbackCascadeTitle(child),
      description: child.patch.description ?? null,
      date: child.patch.dates.air ?? child.patch.dates.firstAir ?? child.patch.dates.release ?? null,
      positionLabel: cascadePositionLabel(child),
      imageUrl: firstImageUrl(child.images, "still") ?? firstImageUrl(child.images, "poster") ?? firstImageUrl(child.images, "backdrop"),
      creditCount: child.patch.credits.length,
      metadataCount: cascadeMetadataCount(child),
      children: nested,
      targetEntityId: child.targetEntityId ?? null,
    };
  }

  function cascadeNodeToCard(node: CascadeNode): EntityThumbnailCard {
    return {
      entity: {
        id: node.proposalId,
        kind: node.kind,
        title: node.title,
        parentEntityId: null,
        sortOrder: null,
        relationships: [],
        capabilities: [],
        childrenByKind: [],
      },
      aspectRatio: cascadeNodeAspectRatio(node),
      cover: node.imageUrl ? { src: node.imageUrl, alt: node.title } : null,
      hover: { kind: "none" },
      subtitle: cascadeNodeSubtitle(node),
    };
  }

  function cascadeNodeAspectRatio(node: CascadeNode): EntityThumbnailCard["aspectRatio"] {
    if (node.kind === "person") return { width: 4, height: 5 };
    if (node.kind === "studio") return "wide";
    if (node.kind.includes("season")) return { width: 2, height: 3 };
    return "video";
  }

  function cascadeNodeSubtitle(node: CascadeNode): string {
    const parts = [
      node.positionLabel,
      node.date,
      node.children.length > 0 ? `${node.children.length} ${node.children.length === 1 ? "child" : "children"}` : null,
      node.creditCount > 0 ? `${node.creditCount} credit${node.creditCount === 1 ? "" : "s"}` : null,
      `${node.metadataCount} fields`,
    ].filter((part): part is string => Boolean(part));
    return parts.join(" / ");
  }

  function fallbackCascadeTitle(child: EntityMetadataProposal): string {
    if (child.targetKind.includes("season")) return `Season ${positionValue(child, ["seasonNumber", "season"]) ?? "?"}`;
    if (child.targetKind.includes("episode")) return `Episode ${positionValue(child, ["episodeNumber", "episode"]) ?? "?"}`;
    return child.targetKind;
  }

  function cascadePositionLabel(child: EntityMetadataProposal): string {
    const season = positionValue(child, ["seasonNumber", "season"]);
    const episode = positionValue(child, ["episodeNumber", "episode"]);
    if (season != null && episode != null) return `S${String(season).padStart(2, "0")}E${String(episode).padStart(2, "0")}`;
    if (season != null) return `S${String(season).padStart(2, "0")}`;
    if (episode != null) return `E${String(episode).padStart(2, "0")}`;
    return child.targetKind;
  }

  function positionValue(child: EntityMetadataProposal, keys: string[]): number | null {
    for (const key of keys) {
      const value = child.patch.positions[key];
      if (typeof value === "number") return value;
    }
    return null;
  }

  function cascadeMetadataCount(child: EntityMetadataProposal): number {
    let count = 0;
    if (child.patch.title) count++;
    if (child.patch.description) count++;
    count += Object.keys(child.patch.externalIds).length;
    count += child.patch.urls.length;
    count += child.patch.tags.length;
    if (child.patch.studio) count++;
    count += child.patch.credits.length;
    count += Object.keys(child.patch.dates).length;
    count += Object.keys(child.patch.stats).length;
    count += Object.keys(child.patch.positions).length;
    if (child.patch.classification) count++;
    count += child.images.length;
    return count;
  }

  function compareCascadeNodes(a: CascadeNode, b: CascadeNode): number {
    const aSeason = cascadeLabelNumber(a.positionLabel, "S");
    const bSeason = cascadeLabelNumber(b.positionLabel, "S");
    if (aSeason !== bSeason) return aSeason - bSeason;
    const aEpisode = cascadeLabelNumber(a.positionLabel, "E");
    const bEpisode = cascadeLabelNumber(b.positionLabel, "E");
    if (aEpisode !== bEpisode) return aEpisode - bEpisode;
    return a.title.localeCompare(b.title);
  }

  function cascadeLabelNumber(label: string, prefix: string): number {
    const match = new RegExp(`${prefix}(\\d+)`, "i").exec(label);
    return match ? Number(match[1]) : Number.MAX_SAFE_INTEGER;
  }

  function cascadeNodeCount(node: CascadeNode): number {
    return 1 + node.children.reduce((sum, child) => sum + cascadeNodeCount(child), 0);
  }

  function selectedCascadeNodeCount(node: CascadeNode): number {
    return (isCascadeSelected(node) ? 1 : 0) + node.children.reduce((sum, child) => sum + selectedCascadeNodeCount(child), 0);
  }

  function cascadeNodeIds(node: CascadeNode): string[] {
    return [node.proposalId, ...node.children.flatMap(cascadeNodeIds)];
  }

  function isCascadeSelected(node: CascadeNode): boolean {
    return selectedCascade[node.proposalId] !== false;
  }

  function defaultCascadeSelection(result: EntityMetadataProposal): Record<string, boolean> {
    const selected: Record<string, boolean> = {};
    for (const child of reviewChildProposals(result)) markCascadeSelected(child, selected);
    return selected;
  }

  function markCascadeSelected(child: EntityMetadataProposal, selected: Record<string, boolean>) {
    selected[child.proposalId] = true;
    for (const nested of reviewChildProposals(child)) markCascadeSelected(nested, selected);
  }

  function defaultImageSelection(images: ImageCandidate[]): Record<string, string | null> {
    const selected: Record<string, string | null> = {};
    for (const group of imageGroups(images)) selected[group.kind] = group.images[0]?.url ?? null;
    return selected;
  }

  function entries(record: Record<string, string | number>): string[] {
    return Object.entries(record).map(([key, value]) => `${key}: ${value}`);
  }

  function mapKind(kind: string): string {
    if (kind === "video_series") return "video-series";
    if (kind === "video_movie" || kind === "video_episode") return "video";
    if (kind.includes("_")) return kind.replaceAll("_", "-");
    return kind;
  }

  function creditKey(credit: CreditPatch, index: number): string {
    return `${credit.role}:${credit.name}:${credit.character ?? ""}:${index}`;
  }

  function creditSubtitle(credit: CreditPatch): string {
    return credit.character?.trim() || credit.role;
  }

  function creditState(credit: CreditPatch): "merge" | "new" {
    const hydrated = activeReviewEntity ? creditNamesFromEntity(activeReviewEntity) : [];
    const names = hydrated.length > 0 ? hydrated : (activeTarget?.existingCreditNames ?? []);
    return isNewRelationshipTitle(credit.name, names)
      ? "new"
      : "merge";
  }

  function tagTitlesFromEntity(entity: EntityDetailCard): string[] {
    return relationshipTitlesByEntityId[entity.id]?.tags ?? [];
  }

  function creditNamesFromEntity(entity: EntityDetailCard): string[] {
    return relationshipTitlesByEntityId[entity.id]?.credits ?? [];
  }

  function firstImageUrl(images: ImageCandidate[], kind: string): string | null {
    return images.find((image) => image.kind === kind)?.url ?? null;
  }

  function imageAspect(kind: string): "poster" | "wide" | "logo" {
    const normalized = kind.toLowerCase();
    if (normalized.includes("logo")) return "logo";
    if (normalized.includes("backdrop") || normalized.includes("still")) return "wide";
    return "poster";
  }

  function readError(err: unknown): string {
    if (!(err instanceof Error)) return "Identify failed";
    try {
      const parsed = JSON.parse(err.message) as { message?: string; detail?: string };
      return parsed.message ?? parsed.detail ?? err.message;
    } catch {
      return err.message;
    }
  }
</script>

<svelte:window onkeydown={handleKeydown} />

{#if installedProviders.length > 0}
  <button
    type="button"
    class={["identify-button", className]}
    disabled={!hasTargets || loadingProviders}
    onclick={() => void openWorkflow()}
  >
    {#if loadingProviders}
      <Loader2 class="h-4 w-4 animate-spin" />
    {:else}
      <ScanSearch class="h-4 w-4" />
    {/if}
    <span>{label}</span>
  </button>
{/if}

{#if workflowOpen}
  <div class="identify-modal" use:portal role="dialog" aria-modal="true" aria-label={`Identify ${activeTarget?.title ?? title}`}>
    <div class="modal-backdrop" aria-hidden="true"></div>
    <div class="modal-panel">
      <!-- Compact toolbar: provider + nav + close -->
      <header class="modal-toolbar">
        <div class="toolbar-left">
          {#if loadingProviders}
            <span class="toolbar-status"><Loader2 class="h-3.5 w-3.5 animate-spin" /> Loading…</span>
          {:else}
            <div class="provider-cards">
              {#each installedProviders as provider (provider.id)}
                {@const isActive = selectedProviderId === provider.id}
                {@const missingAuth = provider.missingAuthKeys.length > 0}
                <button
                  type="button"
                  class="provider-card"
                  class:active={isActive}
                  disabled={missingAuth || identifying}
                  onclick={() => void selectProvider(provider.id)}
                >
                  <Zap class="h-3.5 w-3.5" />
                  <span>{provider.name}</span>
                  {#if missingAuth}
                    <span class="prov-badge missing">Auth</span>
                  {:else if isActive && proposal}
                    <span class="prov-badge matched">Matched</span>
                  {/if}
                </button>
              {/each}
            </div>
          {/if}
        </div>
        <div class="toolbar-right">
          {#if targets.length > 1}
            <div class="nav-pill">
              <button type="button" class="nav-btn" disabled={activeIndex === 0} onclick={() => moveTo(activeIndex - 1)} aria-label="Previous">
                <ChevronLeft class="h-3.5 w-3.5" />
              </button>
              <span class="nav-count">{activeIndex + 1}/{targets.length}</span>
              <button type="button" class="nav-btn" disabled={activeIndex >= targets.length - 1} onclick={() => moveTo(activeIndex + 1)} aria-label="Next">
                <ChevronRight class="h-3.5 w-3.5" />
              </button>
            </div>
          {/if}
          <button type="button" class="close-btn" onclick={closeWorkflow} aria-label="Close">
            <X class="h-4 w-4" />
          </button>
        </div>
      </header>

      <!-- Body -->
      <div class="modal-body" bind:this={modalBodyElement}>
        {#if error}
          <div class="error-box" role="alert">
            <AlertCircle class="h-4 w-4" />
            <span>{error}</span>
          </div>
        {/if}

        {#if installedProviders.length === 0 && !loadingProviders}
          <div class="empty-state">
            <AlertCircle class="h-5 w-5" />
            <span>No installed identify plugin supports this entity type.</span>
          </div>
        {:else if runnableProviders.length === 0 && !loadingProviders}
          <div class="empty-state">
            <AlertCircle class="h-5 w-5" />
            <span>Installed identify plugins need credentials before they can run.</span>
          </div>
        {:else if identifying}
          <div class="empty-state">
            <div class="scan-animation"><Loader2 class="h-5 w-5 animate-spin" /></div>
            <span>Searching with {selectedProvider?.name ?? "provider"}…</span>
          </div>
        {:else if !proposal && !loadingProviders}
          <div class="empty-state">
            <Sparkles class="h-5 w-5" />
            <span>Select a provider to search for metadata.</span>
          </div>
        {:else if proposal && activeProposal}
          <div class="review-sections">
            <!-- Match info bar -->
            <div class="match-bar">
              {#if reviewPath.length > 0}
                <button type="button" class="scope-back" onclick={leaveReviewScope}>
                  <ChevronLeft class="h-3 w-3" />
                  Back
                </button>
              {/if}
              <span class="match-badge">
                <Eye class="h-3 w-3" />
                {activeProposal.matchReason ?? "match"}
              </span>
              <span class="match-provider">{activeProposal.provider}</span>
              <span class="match-kind">{reviewKind}</span>
              <span class="scope-title">{reviewTitle}</span>
              {#if loadingViewEntity}
                <span class="match-alt">Loading entity…</span>
              {/if}
              {#if activeProposal.candidates.length > 1}
                <span class="match-sep">·</span>
                <span class="match-alt">{activeProposal.candidates.length} candidates</span>
              {/if}
            </div>

            <!-- Candidates -->
            {#if activeProposal.candidates.length > 1}
              <section class="section-card">
                <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('candidates')} onkeydown={(e) => e.key === 'Enter' && toggleSection('candidates')}>
                  <h4>Other matches</h4>
                  <div class="section-meta">
                    <span class="count-badge">{activeProposal.candidates.length}</span>
                    <span class="chevron" class:rotated={!expandedSections.candidates}><ChevronDown class="h-3.5 w-3.5" /></span>
                  </div>
                </div>
                {#if expandedSections.candidates}
                  <div class="section-body">
                    <div class="candidate-list">
                      {#each activeProposal.candidates as candidate (candidate.externalIds.tmdb ?? candidate.title)}
                        <button type="button" class="candidate-card" onclick={() => rerunCandidate(candidate)}>
                          {#if candidate.posterUrl}
                            <img src={candidate.posterUrl} alt="" class="candidate-poster" />
                          {:else}
                            <div class="candidate-poster-empty"><ImageIcon class="h-4 w-4" /></div>
                          {/if}
                          <div class="candidate-info">
                            <strong>{candidate.title}</strong>
                            <small>{candidate.year ?? "Unknown year"}</small>
                          </div>
                          <ChevronRight class="h-3.5 w-3.5" />
                        </button>
                      {/each}
                    </div>
                  </div>
                {/if}
              </section>
            {/if}

            <!-- Scalar Fields -->
            <section class="section-card">
              <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('fields')} onkeydown={(e) => e.key === 'Enter' && toggleSection('fields')}>
                <h4>Fields</h4>
                <div class="section-meta">
                  <span class="count-badge">{scalarFieldKeys.filter((f) => selectedFields[f]).length} / {scalarFieldKeys.filter((f) => hasField(activeProposal, f)).length}</span>
                  <span class="chevron" class:rotated={!expandedSections.fields}><ChevronDown class="h-3.5 w-3.5" /></span>
                </div>
              </div>
              {#if expandedSections.fields}
                <div class="section-body">
                  <div class="field-list">
                    {#each scalarFieldKeys as field (field)}
                      {#if hasField(activeProposal, field)}
                        <button
                          type="button"
                          class="field-row"
                          class:active={selectedFields[field]}
                          onclick={() => toggleField(field)}
                        >
                          <div class="field-check">
                            {#if selectedFields[field]}
                              <Check class="h-3 w-3" />
                            {/if}
                          </div>
                          <span class="field-label">{fieldLabels[field]}</span>
                          <span class="field-arrow">→</span>
                          <span class="field-new-value" class:field-wrap={field === "description"}>{fieldValue(activeProposal, field)}</span>
                        </button>
                      {/if}
                    {/each}
                  </div>
                </div>
              {/if}
            </section>

            <!-- Tags — individually selectable -->
            {#if activeProposal.patch.tags.length > 0}
              <section class="section-card">
                <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('tags')} onkeydown={(e) => e.key === 'Enter' && toggleSection('tags')}>
                  <h4>Tags</h4>
                  <div class="section-meta">
                    <span class="count-badge">{selectedTagCount} / {activeProposal.patch.tags.length}</span>
                    <span class="chevron" class:rotated={!expandedSections.tags}><ChevronDown class="h-3.5 w-3.5" /></span>
                  </div>
                </div>
                {#if expandedSections.tags}
                  <div class="section-body">
                    <div class="tag-cloud">
                      {#each activeProposal.patch.tags as tag (tag)}
                        <button
                          type="button"
                          class="tag-select"
                          class:active={selectedTags[tag]}
                          class:is-new={isNewTag(tag)}
                          onclick={() => toggleTag(tag)}
                        >
                          <span class="tag-check">
                            {#if selectedTags[tag]}
                              <Check class="h-2.5 w-2.5" />
                            {/if}
                          </span>
                          <span>{tag}</span>
                          {#if isNewTag(tag)}
                            <span class="tag-new-label">NEW</span>
                          {/if}
                        </button>
                      {/each}
                    </div>
                  </div>
                {/if}
              </section>
            {/if}

            <!-- Studio -->
            {#if activeProposal.patch.studio && studioCard && !hasRelatedStudioProposals}
              <section class="section-card" class:muted={!selectedFields.studio}>
                <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('studio')} onkeydown={(e) => e.key === 'Enter' && toggleSection('studio')}>
                  <h4>Studio</h4>
                  <div class="section-meta">
                    <button
                      type="button"
                      class="toggle-pill"
                      class:included={selectedFields.studio}
                      onclick={(e) => { e.stopPropagation(); toggleField("studio"); }}
                    >
                      {selectedFields.studio ? "Included" : "Excluded"}
                    </button>
                    <span class="chevron" class:rotated={!expandedSections.studio}><ChevronDown class="h-3.5 w-3.5" /></span>
                  </div>
                </div>
                {#if expandedSections.studio}
                  <div class="section-body">
                    <div class="studio-row">
                      <EntityThumbnail card={studioCard} titleAlign="center" titleSize="compact" linkable={false} />
                    </div>
                  </div>
                {/if}
              </section>
            {/if}

            <!-- Credits -->
            {#if activeProposal.patch.credits.length > 0 && !hasRelatedPersonProposals}
              <section class="section-card" class:muted={!selectedFields.credits}>
                <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('credits')} onkeydown={(e) => e.key === 'Enter' && toggleSection('credits')}>
                  <h4>Cast & Crew</h4>
                  <div class="section-meta">
                    <button
                      type="button"
                      class="toggle-pill"
                      class:included={selectedFields.credits}
                      onclick={(e) => { e.stopPropagation(); toggleField("credits"); }}
                    >
                      {selectedFields.credits ? "Included" : "Excluded"}
                    </button>
                    <span class="count-badge">{Object.values(selectedCredits).filter(Boolean).length} / {activeProposal.patch.credits.length}</span>
                    <span class="chevron" class:rotated={!expandedSections.credits}><ChevronDown class="h-3.5 w-3.5" /></span>
                  </div>
                </div>
                {#if expandedSections.credits}
                  <div class="section-body">
                    <div class="credit-scroller">
                      {#each activeProposal.patch.credits as credit, index (creditKey(credit, index))}
                        {@const key = creditKey(credit, index)}
                        {@const state = creditState(credit)}
                        <div class="credit-thumbnail">
                          <EntityThumbnail
                            card={creditCards[index]}
                            titleAlign="center"
                            titleSize="compact"
                            linkable={false}
                            selectable
                            selected={selectedCredits[key] !== false}
                            onSelectedChange={() => toggleCredit(key)}
                          >
                            {#snippet subtitleContent()}
                              <span class="credit-role-label">{creditSubtitle(credit)}</span>
                              {#if state === "new"}
                                <span class="credit-new-label">NEW</span>
                              {/if}
                            {/snippet}
                          </EntityThumbnail>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/if}
              </section>
            {/if}

            <!-- Artwork -->
            {#if reviewableImageGroups.length > 0}
              <section class="section-card" class:muted={!selectedFields.images}>
                <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('artwork')} onkeydown={(e) => e.key === 'Enter' && toggleSection('artwork')}>
                  <h4>Artwork</h4>
                  <div class="section-meta">
                    <button
                      type="button"
                      class="toggle-pill"
                      class:included={selectedFields.images}
                      onclick={(e) => { e.stopPropagation(); toggleField("images"); }}
                    >
                      {selectedFields.images ? "Included" : "Excluded"}
                    </button>
                    <span class="chevron" class:rotated={!expandedSections.artwork}><ChevronDown class="h-3.5 w-3.5" /></span>
                  </div>
                </div>
                {#if expandedSections.artwork}
                  <div class="section-body">
                    <div class="art-grid">
                      {#each reviewableImageGroups as group (group.kind)}
                        {@const sel = selectedImages[group.kind]}
                        <div class="art-card" class:active={!!sel}>
                          <div class="art-card-header">
                            <button type="button" class="art-card-check" class:active={!!sel} onclick={() => toggleImageSelection(group)}>
                              <div class="field-check">
                                {#if sel}
                                  <Check class="h-3 w-3" />
                                {/if}
                              </div>
                            </button>
                            <span class="art-kind">{group.kind}</span>
                            <span class="art-count">{group.images.length} option{group.images.length === 1 ? "" : "s"}</span>
                          </div>
                          <button type="button" class="art-card-preview" onclick={() => openLightbox(group.kind)}>
                            {#if sel}
                              <img src={sel} alt="{group.kind} preview" class="art-preview-img" data-aspect={imageAspect(group.kind)} />
                            {:else}
                              <div class="art-preview-empty" data-aspect={imageAspect(group.kind)}>
                                <ImageIcon class="h-6 w-6" />
                                <span>Select image</span>
                              </div>
                            {/if}
                            <div class="art-browse-hint">
                              <span>Browse</span>
                              <ChevronRight class="h-3.5 w-3.5" />
                            </div>
                          </button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/if}
              </section>
            {/if}

            <!-- Children -->
            {#if childCascadeRows.length > 0}
              <section class="section-card">
                <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('seasons')} onkeydown={(e) => e.key === 'Enter' && toggleSection('seasons')}>
                  <h4>Children</h4>
                  <div class="section-meta">
                    <span class="count-badge">{childCascadeSelectedCount} / {childCascadeTotalCount}</span>
                    <span class="chevron" class:rotated={!expandedSections.seasons}><ChevronDown class="h-3.5 w-3.5" /></span>
                  </div>
                </div>
                {#if expandedSections.seasons}
                  <div class="section-body">
                    <div class="proposal-row-stack">
                      {#each childCascadeRows as row (row.id)}
                        <div class="proposal-row">
                          <div class="proposal-row-header">
                            <span>{row.label}</span>
                            <small>{row.nodes.filter(isCascadeSelected).length} / {row.nodes.length}</small>
                          </div>
                          <div class="proposal-thumbnail-strip">
                            {#each row.nodes as node (node.proposalId)}
                              <div class="proposal-thumbnail" class:muted={!isCascadeSelected(node)}>
                                <EntityThumbnail
                                  card={cascadeNodeToCard(node)}
                                  titleAlign="center"
                                  titleSize="compact"
                                  linkable={false}
                                  selectable
                                  selected={isCascadeSelected(node)}
                                  onActivate={() => enterReviewScope(node)}
                                  onSelectedChange={(selected) => setCascadeNodeSelected(node, selected)}
                                />
                              </div>
                            {/each}
                          </div>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/if}
              </section>
            {/if}

            <!-- Related -->
            {#if relatedCascadeRows.length > 0}
              <section class="section-card">
                <div class="section-header" role="button" tabindex="0" onclick={() => toggleSection('related')} onkeydown={(e) => e.key === 'Enter' && toggleSection('related')}>
                  <h4>Related</h4>
                  <div class="section-meta">
                    <span class="count-badge">{relatedCascadeSelectedCount} / {relatedCascadeTotalCount}</span>
                    <span class="chevron" class:rotated={!expandedSections.related}><ChevronDown class="h-3.5 w-3.5" /></span>
                  </div>
                </div>
                {#if expandedSections.related}
                  <div class="section-body">
                    <div class="proposal-row-stack">
                      {#each relatedCascadeRows as row (row.id)}
                        <div class="proposal-row">
                          <div class="proposal-row-header">
                            <span>{row.label}</span>
                            <small>{row.nodes.filter(isCascadeSelected).length} / {row.nodes.length}</small>
                          </div>
                          <div class="proposal-thumbnail-strip">
                            {#each row.nodes as node (node.proposalId)}
                              <div class="proposal-thumbnail" class:muted={!isCascadeSelected(node)}>
                                <EntityThumbnail
                                  card={cascadeNodeToCard(node)}
                                  titleAlign="center"
                                  titleSize="compact"
                                  linkable={false}
                                  selectable
                                  selected={isCascadeSelected(node)}
                                  onActivate={() => enterReviewScope(node)}
                                  onSelectedChange={(selected) => setCascadeNodeSelected(node, selected)}
                                />
                              </div>
                            {/each}
                          </div>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/if}
              </section>
            {/if}

          </div>
        {/if}
      </div>

      <!-- Footer -->
      <footer class="modal-footer">
        <button type="button" class="btn-ghost" onclick={closeWorkflow}>Cancel</button>
        <div class="footer-actions">
          {#if targets.length > 1 && proposal}
            <button type="button" class="btn-secondary" disabled={applying} onclick={() => void apply(false)}>
              Apply & next
              <ChevronRight class="h-3.5 w-3.5" />
            </button>
          {/if}
          <button type="button" class="btn-primary" disabled={!proposal || applying} onclick={() => void apply(true)}>
            {#if applying}
              <Loader2 class="h-4 w-4 animate-spin" />
              Applying…
            {:else}
              <Check class="h-4 w-4" />
              Apply
            {/if}
          </button>
        </div>
      </footer>
    </div>
  </div>
{/if}

<!-- Lightbox overlay — slides in from right, same size as main modal -->
{#if lightboxGroup && activeProposal}
  <div class="lightbox" use:portal role="dialog" aria-modal="true" aria-label={`Select ${lightboxGroup}`}>
    <div class="lightbox-backdrop" onclick={closeLightbox} aria-hidden="true"></div>
    <div class="lightbox-panel">
      <header class="lightbox-header">
        <h3>{lightboxGroup}</h3>
        <button type="button" class="btn-primary" onclick={closeLightbox}>
          <Check class="h-3.5 w-3.5" />
          Confirm
        </button>
      </header>

      <div class="lightbox-focus">
        {#if lightboxSelectedUrl}
          <img src={lightboxSelectedUrl} alt="{lightboxGroup} preview" />
        {:else}
          <div class="lightbox-empty"><ImageIcon class="h-8 w-8" /><span>No image selected</span></div>
        {/if}
      </div>

      <div class="lightbox-strip">
        {#each lightboxImages as image (image.url)}
          <button
            type="button"
            class="lightbox-thumb"
            class:active={selectedImages[lightboxGroup] === image.url}
            onclick={() => setImageSelection(lightboxGroup!, image.url)}
          >
            <img src={image.url} alt="" data-aspect={imageAspect(lightboxGroup)} />
            {#if selectedImages[lightboxGroup] === image.url}
              <div class="thumb-check"><Check class="h-3 w-3" /></div>
            {/if}
          </button>
        {/each}
      </div>
    </div>
  </div>
{/if}

<style>
  /* === Base resets === */
  button { border-radius: 0; }
  button { cursor: pointer; }
  button:disabled { cursor: not-allowed; opacity: 0.5; }

  /* === Trigger button === */
  .identify-button {
    display: inline-flex;
    min-height: 2.1rem;
    align-items: center;
    justify-content: center;
    gap: 0.45rem;
    border: 1px solid rgba(196, 154, 90, 0.55);
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-2, #111827);
    color: var(--color-text);
    padding: 0 0.65rem;
    font-size: 0.76rem;
    box-shadow: 0 0 14px rgba(196, 154, 90, 0.12);
    transition: box-shadow 0.2s, border-color 0.2s;
  }
  .identify-button:hover:not(:disabled) {
    border-color: rgba(196, 154, 90, 0.8);
    box-shadow: 0 0 20px rgba(196, 154, 90, 0.22);
  }

  /* === Modal shell === */
  .identify-modal {
    position: fixed;
    inset: 0;
    z-index: 1500;
    display: flex;
    isolation: isolate;
    animation: modal-in 0.25s ease-out;
  }

  @keyframes modal-in {
    from { opacity: 0; }
    to { opacity: 1; }
  }

  .modal-backdrop {
    position: absolute;
    inset: 0;
    z-index: 0;
    background:
      radial-gradient(ellipse at 30% 20%, rgba(196, 154, 90, 0.06) 0%, transparent 50%),
      rgba(4, 5, 8, 0.92);
    backdrop-filter: blur(4px);
  }

  .modal-panel {
    position: relative;
    z-index: 1;
    display: flex;
    width: 100%;
    min-width: 0;
    overflow: hidden;
    height: 100dvh;
    max-height: 100dvh;
    flex-direction: column;
    border: 1px solid var(--color-border, #1c2235);
    background: linear-gradient(180deg, rgb(12, 15, 21) 0%, rgb(8, 10, 15) 100%);
    color: var(--color-text);
    box-shadow: 0 32px 100px rgba(0, 0, 0, 0.7), inset 0 1px 0 rgba(255, 255, 255, 0.03);
    animation: panel-in 0.3s ease-out;
  }

  @keyframes panel-in {
    from { opacity: 0; transform: scale(0.97) translateY(8px); }
    to { opacity: 1; transform: scale(1) translateY(0); }
  }

  /* === Toolbar === */
  .modal-toolbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    flex-shrink: 0;
    border-bottom: 1px solid var(--color-border, #1c2235);
    background: rgba(12, 15, 21, 0.97);
    padding: 0.55rem 0.85rem;
  }

  .toolbar-left {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    min-width: 0;
    flex: 1;
  }

  .toolbar-status {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.72rem;
  }

  .toolbar-right {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    flex-shrink: 0;
  }

  /* === Provider cards === */
  .provider-cards {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
  }

  .provider-card {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-muted, #8a93a6);
    padding: 0.35rem 0.55rem;
    font-size: 0.68rem;
    transition: all 0.15s;
  }
  .provider-card:hover:not(:disabled) {
    border-color: rgba(196, 154, 90, 0.4);
    color: var(--color-text-primary, #f2eed8);
    background: rgba(196, 154, 90, 0.06);
  }
  .provider-card.active {
    border-color: rgba(196, 154, 90, 0.6);
    color: var(--color-text-primary, #f2eed8);
    background: linear-gradient(135deg, rgba(196, 154, 90, 0.12), rgba(196, 154, 90, 0.04));
    box-shadow: 0 0 12px rgba(196, 154, 90, 0.1);
  }

  .prov-badge {
    padding: 0.1rem 0.3rem;
    font-size: 0.52rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }
  .prov-badge.missing {
    border: 1px solid rgba(168, 72, 80, 0.4);
    color: var(--color-status-error-text, #cc7880);
  }
  .prov-badge.matched {
    border: 1px solid rgba(78, 138, 98, 0.4);
    color: var(--color-status-success-text, #80b898);
  }

  /* === Nav pill === */
  .nav-pill {
    display: flex;
    align-items: center;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
  }
  .nav-btn {
    display: grid;
    width: 1.6rem;
    height: 1.6rem;
    place-items: center;
    border: none;
    background: transparent;
    color: var(--color-text-muted, #8a93a6);
    transition: color 0.15s, background 0.15s;
  }
  .nav-btn:hover:not(:disabled) {
    color: var(--color-text-primary, #f2eed8);
    background: rgba(255, 255, 255, 0.04);
  }
  .nav-count {
    padding: 0 0.3rem;
    color: var(--color-text-muted, #8a93a6);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.6rem;
  }

  /* === Close button === */
  .close-btn {
    display: grid;
    width: 1.8rem;
    height: 1.8rem;
    place-items: center;
    border: 1px solid var(--color-border, #1c2235);
    background: transparent;
    color: var(--color-text-muted, #8a93a6);
    transition: color 0.15s, border-color 0.15s, background 0.15s;
  }
  .close-btn:hover {
    color: var(--color-text-primary, #f2eed8);
    border-color: rgba(168, 72, 80, 0.5);
    background: rgba(168, 72, 80, 0.1);
  }

  /* === Body === */
  .modal-body {
    min-height: 0;
    min-width: 0;
    flex: 1;
    overflow: auto;
    padding: 0.85rem;
    scrollbar-width: thin;
    scrollbar-color: rgba(196, 154, 90, 0.2) transparent;
  }

  /* === Empty states === */
  .empty-state {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 0.6rem;
    min-height: 14rem;
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.78rem;
    text-align: center;
  }

  .scan-animation {
    display: grid;
    width: 3rem;
    height: 3rem;
    place-items: center;
    border: 1px solid rgba(196, 154, 90, 0.3);
    background: linear-gradient(135deg, rgba(196, 154, 90, 0.08), transparent);
    color: var(--color-text-accent, #c49a5a);
    box-shadow: 0 0 20px rgba(196, 154, 90, 0.12);
    animation: pulse-glow 2s ease-in-out infinite;
  }

  @keyframes pulse-glow {
    0%, 100% { box-shadow: 0 0 12px rgba(196, 154, 90, 0.08); }
    50% { box-shadow: 0 0 28px rgba(196, 154, 90, 0.22); }
  }

  .error-box {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    margin-bottom: 0.75rem;
    border: 1px solid rgba(168, 72, 80, 0.4);
    background: rgba(90, 44, 48, 0.15);
    color: var(--color-status-error-text, #cc7880);
    padding: 0.6rem 0.75rem;
    font-size: 0.74rem;
  }

  /* === Review sections === */
  .review-sections {
    display: grid;
    gap: 0.6rem;
    min-width: 0;
  }

  /* === Match bar === */
  .match-bar {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.5rem;
    padding: 0.5rem 0.65rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    font-size: 0.65rem;
  }

  .match-badge {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    border: 1px solid rgba(78, 138, 98, 0.4);
    background: rgba(42, 74, 56, 0.25);
    color: var(--color-status-success-text, #80b898);
    padding: 0.15rem 0.45rem;
    font-size: 0.58rem;
    text-transform: capitalize;
  }

  .match-provider {
    color: var(--color-text-muted, #8a93a6);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.6rem;
  }

  .match-kind {
    border: 1px solid var(--color-border, #1c2235);
    color: var(--color-text-muted, #8a93a6);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.55rem;
    padding: 0.1rem 0.35rem;
    text-transform: uppercase;
  }

  .match-sep {
    color: var(--color-text-disabled, #4a5260);
  }

  .match-alt {
    color: var(--color-text-disabled, #4a5260);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.58rem;
  }

  .scope-back {
    display: inline-flex;
    align-items: center;
    gap: 0.2rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    color: var(--color-text-secondary, #c4c9d4);
    padding: 0.16rem 0.4rem;
    font-size: 0.58rem;
  }

  .scope-title {
    min-width: 0;
    overflow-wrap: anywhere;
    color: var(--color-text-primary, #f2eed8);
    font-size: 0.64rem;
  }

  /* === Section cards === */
  .section-card {
    min-width: 0;
    border: 1px solid var(--color-border, #1c2235);
    background: rgba(12, 15, 21, 0.6);
    transition: opacity 0.2s;
  }
  .section-card.muted {
    opacity: 0.4;
  }

  .section-header {
    display: flex;
    width: 100%;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    border: none;
    border-bottom: 1px solid transparent;
    background: transparent;
    color: var(--color-text-primary, #f2eed8);
    padding: 0.6rem 0.75rem;
    cursor: pointer;
    user-select: none;
    transition: background 0.15s;
  }
  .section-header:hover {
    background: rgba(255, 255, 255, 0.02);
  }
  .section-header h4 {
    margin: 0;
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  .section-meta {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 0.4rem;
  }

  .chevron {
    display: inline-flex;
    color: var(--color-text-disabled, #4a5260);
    transition: transform 0.2s;
  }
  .chevron.rotated {
    transform: rotate(-90deg);
  }

  .count-badge {
    padding: 0.12rem 0.35rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.55rem;
  }

  .toggle-pill {
    padding: 0.18rem 0.45rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.55rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    transition: all 0.15s;
  }
  .toggle-pill.included {
    border-color: rgba(78, 138, 98, 0.4);
    background: rgba(42, 74, 56, 0.2);
    color: var(--color-status-success-text, #80b898);
  }

  .section-body {
    min-width: 0;
    padding: 0 0.75rem 0.75rem;
  }

  /* === Field rows === */
  .field-list {
    display: grid;
    gap: 0.25rem;
  }

  .field-row {
    display: grid;
    grid-template-columns: auto minmax(6rem, 0.8fr) auto minmax(0, 1.8fr);
    gap: 0.5rem;
    align-items: center;
    width: 100%;
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-muted, #8a93a6);
    padding: 0.45rem 0.55rem;
    text-align: left;
    transition: all 0.15s;
  }
  .field-row:hover {
    background: rgba(255, 255, 255, 0.02);
    border-color: rgba(255, 255, 255, 0.04);
  }
  .field-row.active {
    border-color: rgba(196, 154, 90, 0.35);
    background: rgba(196, 154, 90, 0.04);
  }

  .field-check {
    display: grid;
    width: 1.15rem;
    height: 1.15rem;
    flex-shrink: 0;
    place-items: center;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    transition: all 0.15s;
  }
  .field-row.active .field-check {
    border-color: rgba(196, 154, 90, 0.6);
    background: linear-gradient(135deg, rgba(196, 154, 90, 0.3), rgba(196, 154, 90, 0.15));
    color: var(--color-text-accent, #c49a5a);
  }
  .field-check.active {
    border-color: rgba(196, 154, 90, 0.6);
    background: linear-gradient(135deg, rgba(196, 154, 90, 0.3), rgba(196, 154, 90, 0.15));
    color: var(--color-text-accent, #c49a5a);
  }

  .field-label {
    font-size: 0.68rem;
    font-weight: 500;
    color: var(--color-text-secondary, #c4c9d4);
  }

  .field-arrow {
    color: var(--color-text-disabled, #4a5260);
    font-size: 0.72rem;
    font-weight: 600;
  }
  .field-row.active .field-arrow {
    color: var(--color-text-accent, #c49a5a);
  }

  .field-new-value {
    min-width: 0;
    overflow: hidden;
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.68rem;
    overflow-wrap: anywhere;
    text-overflow: ellipsis;
    white-space: normal;
    display: -webkit-box;
    -webkit-box-orient: vertical;
    -webkit-line-clamp: 2;
    line-clamp: 2;
  }
  .field-new-value.field-wrap {
    -webkit-line-clamp: 4;
    line-clamp: 4;
    line-height: 1.45;
  }
  .field-row.active .field-new-value {
    color: var(--color-text-primary, #f2eed8);
  }

  /* === Tags — individually selectable chips === */
  .tag-cloud {
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem;
  }

  .tag-select {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-muted, #8a93a6);
    padding: 0.25rem 0.5rem;
    font-size: 0.62rem;
    font-weight: 500;
    letter-spacing: 0.03em;
    text-transform: uppercase;
    transition: all 0.15s;
  }
  .tag-select:hover {
    border-color: rgba(255, 255, 255, 0.1);
    background: rgba(255, 255, 255, 0.03);
  }
  .tag-select.active {
    border-color: rgba(196, 154, 90, 0.45);
    background: rgba(196, 154, 90, 0.08);
    color: var(--color-text-primary, #f2eed8);
  }
  .tag-select.is-new.active {
    border-color: rgba(78, 138, 98, 0.5);
    background: rgba(42, 74, 56, 0.15);
  }

  .tag-check {
    display: grid;
    width: 0.9rem;
    height: 0.9rem;
    place-items: center;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    transition: all 0.15s;
  }
  .tag-select.active .tag-check {
    border-color: rgba(196, 154, 90, 0.6);
    background: linear-gradient(135deg, rgba(196, 154, 90, 0.3), rgba(196, 154, 90, 0.15));
    color: var(--color-text-accent, #c49a5a);
  }
  .tag-select.is-new.active .tag-check {
    border-color: rgba(78, 138, 98, 0.5);
    background: linear-gradient(135deg, rgba(78, 138, 98, 0.3), rgba(78, 138, 98, 0.15));
    color: var(--color-status-success-text, #80b898);
  }

  .tag-new-label {
    padding: 0.05rem 0.25rem;
    border: 1px solid rgba(78, 138, 98, 0.4);
    color: var(--color-status-success-text, #80b898);
    font-size: 0.48rem;
    letter-spacing: 0.06em;
  }

  /* === Studio === */
  .studio-row {
    display: flex;
    gap: 0.75rem;
  }
  .studio-row :global(.entity-thumbnail) {
    flex: 0 1 clamp(8rem, 45vw, 12rem);
    max-width: 100%;
  }

  /* === Credits — horizontal EntityThumbnail scroller === */
  .credit-scroller {
    display: grid;
    max-width: 100%;
    min-width: 0;
    grid-auto-columns: clamp(5.5rem, 22vw, 7.5rem);
    grid-auto-flow: column;
    gap: 0.6rem;
    overflow-x: auto;
    overflow-y: hidden;
    scroll-padding-inline: 0.25rem;
    scrollbar-width: thin;
    scrollbar-color: rgba(196, 154, 90, 0.15) transparent;
    padding-bottom: 0.25rem;
  }

  .credit-thumbnail {
    min-width: 0;
    width: 100%;
  }

  .credit-thumbnail :global(.entity-thumbnail) {
    min-width: 0;
    width: 100%;
  }

  .proposal-row-stack {
    display: grid;
    gap: 0.8rem;
    min-width: 0;
  }

  .proposal-row {
    display: grid;
    gap: 0.45rem;
    min-width: 0;
  }

  .proposal-row-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    color: var(--color-text-muted, #8a93a6);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.62rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  .proposal-row-header small {
    color: var(--color-text-disabled, #4a5260);
    font-size: 0.58rem;
  }

  .proposal-thumbnail-strip {
    display: grid;
    max-width: 100%;
    min-width: 0;
    grid-auto-columns: clamp(5.8rem, 24vw, 8rem);
    grid-auto-flow: column;
    gap: 0.6rem;
    overflow-x: auto;
    overflow-y: hidden;
    scroll-padding-inline: 0.25rem;
    scrollbar-width: thin;
    scrollbar-color: rgba(196, 154, 90, 0.15) transparent;
    padding-bottom: 0.25rem;
  }

  .proposal-thumbnail {
    min-width: 0;
    width: 100%;
    transition: opacity 0.15s;
  }

  .proposal-thumbnail.muted {
    opacity: 0.45;
  }

  .proposal-thumbnail :global(.entity-thumbnail) {
    min-width: 0;
    width: 100%;
  }

  .credit-role-label {
    display: block;
    overflow: hidden;
    padding: 0.1rem 0.3rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-muted, #8a93a6);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.52rem;
    text-align: center;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .credit-new-label {
    display: block;
    margin-top: 0.15rem;
    padding: 0.05rem 0.25rem;
    border: 1px solid rgba(78, 138, 98, 0.4);
    color: var(--color-status-success-text, #80b898);
    font-size: 0.48rem;
    font-family: "JetBrains Mono", monospace;
    text-align: center;
    letter-spacing: 0.06em;
  }

  /* === Artwork cards === */
  .art-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 7rem), 9rem));
    gap: 0.5rem;
    justify-content: start;
  }

  .art-card {
    width: 100%;
    max-width: 9rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    transition: border-color 0.15s;
  }
  .art-card.active {
    border-color: rgba(196, 154, 90, 0.35);
  }

  .art-card-header {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.4rem 0.55rem;
    border-bottom: 1px solid var(--color-border, #1c2235);
  }

  .art-card-check {
    border: none;
    background: transparent;
    padding: 0;
    color: var(--color-text-disabled, #4a5260);
    transition: color 0.15s;
  }
  .art-card-check.active {
    color: var(--color-text-accent, #c49a5a);
  }

  .art-kind {
    font-size: 0.6rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--color-text-muted, #8a93a6);
  }

  .art-count {
    margin-left: auto;
    color: var(--color-text-disabled, #4a5260);
    font-family: "JetBrains Mono", monospace;
    font-size: 0.52rem;
  }

  .art-card-preview {
    position: relative;
    display: block;
    width: 100%;
    border: none;
    background: transparent;
    padding: 0;
    text-align: left;
    transition: opacity 0.15s;
  }
  .art-card-preview:hover {
    opacity: 0.85;
  }

  .art-preview-img {
    display: block;
    width: 100%;
    object-fit: cover;
  }
  .art-preview-img[data-aspect="poster"] { aspect-ratio: 2 / 3; }
  .art-preview-img[data-aspect="wide"] { aspect-ratio: 16 / 9; }

  .art-preview-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 0.3rem;
    width: 100%;
    background: var(--color-surface-2, #101420);
    color: var(--color-text-disabled, #4a5260);
    font-size: 0.6rem;
  }
  .art-preview-empty[data-aspect="poster"] { aspect-ratio: 2 / 3; }
  .art-preview-empty[data-aspect="wide"] { aspect-ratio: 16 / 9; }

  .art-browse-hint {
    position: absolute;
    right: 0.4rem;
    bottom: 0.4rem;
    display: flex;
    align-items: center;
    gap: 0.2rem;
    padding: 0.2rem 0.4rem;
    background: rgba(5, 6, 9, 0.8);
    backdrop-filter: blur(4px);
    color: var(--color-text-secondary, #c4c9d4);
    font-size: 0.55rem;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.04em;
  }

  /* === Candidates === */
  .candidate-list {
    display: grid;
    gap: 0.3rem;
  }

  .candidate-card {
    display: grid;
    grid-template-columns: minmax(1.8rem, 2.2rem) minmax(0, 1fr) auto;
    gap: 0.5rem;
    align-items: center;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-primary, #f2eed8);
    padding: 0.4rem 0.5rem;
    text-align: left;
    transition: all 0.15s;
  }
  .candidate-card:hover {
    border-color: rgba(196, 154, 90, 0.3);
    background: rgba(196, 154, 90, 0.04);
  }

  .candidate-poster {
    width: 2.2rem;
    aspect-ratio: 2 / 3;
    object-fit: cover;
    border: 1px solid var(--color-border, #1c2235);
  }
  .candidate-poster-empty {
    display: grid;
    width: 2.2rem;
    aspect-ratio: 2 / 3;
    place-items: center;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    color: var(--color-text-disabled, #4a5260);
  }

  .candidate-info {
    display: grid;
    gap: 0.08rem;
    min-width: 0;
  }
  .candidate-info strong {
    overflow: hidden;
    font-size: 0.72rem;
    font-weight: 550;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .candidate-info small {
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.6rem;
  }

  /* === Footer === */
  .modal-footer {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    flex-shrink: 0;
    border-top: 1px solid var(--color-border, #1c2235);
    background: rgba(10, 12, 17, 0.97);
    padding: 0.6rem 0.85rem;
  }

  .footer-actions {
    display: flex;
    align-items: center;
    gap: 0.35rem;
  }

  .btn-ghost, .btn-secondary, .btn-primary {
    display: inline-flex;
    min-height: 2rem;
    align-items: center;
    justify-content: center;
    gap: 0.35rem;
    padding: 0 0.7rem;
    font-size: 0.7rem;
    font-weight: 500;
    transition: all 0.15s;
  }

  .btn-ghost {
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-muted, #8a93a6);
  }
  .btn-ghost:hover {
    color: var(--color-text-primary, #f2eed8);
    background: rgba(255, 255, 255, 0.03);
  }

  .btn-secondary {
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    color: var(--color-text-secondary, #c4c9d4);
  }
  .btn-secondary:hover:not(:disabled) {
    border-color: rgba(196, 154, 90, 0.3);
    color: var(--color-text-primary, #f2eed8);
  }

  .btn-primary {
    border: 1px solid rgba(196, 154, 90, 0.6);
    background: linear-gradient(135deg, rgba(196, 154, 90, 0.22), rgba(196, 154, 90, 0.08));
    color: var(--color-text-primary, #f2eed8);
    box-shadow: 0 0 16px rgba(196, 154, 90, 0.12);
  }
  .btn-primary:hover:not(:disabled) {
    border-color: rgba(196, 154, 90, 0.8);
    box-shadow: 0 0 24px rgba(196, 154, 90, 0.2), 0 0 8px rgba(196, 154, 90, 0.15);
  }
  .btn-primary:disabled {
    border-color: var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    box-shadow: none;
  }

  /* === Lightbox — same size as main modal, slide-in from right === */
  .lightbox {
    position: fixed;
    inset: 0;
    z-index: 1600;
    display: flex;
    isolation: isolate;
    animation: modal-in 0.2s ease-out;
  }

  .lightbox-backdrop {
    position: absolute;
    inset: 0;
    z-index: 0;
    background: rgba(2, 3, 5, 0.94);
    backdrop-filter: blur(6px);
  }

  @keyframes slide-in-right {
    from { transform: translateX(100%); opacity: 0.6; }
    to { transform: translateX(0); opacity: 1; }
  }

  .lightbox-panel {
    position: relative;
    z-index: 1;
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100dvh;
    max-height: 100dvh;
    border: 1px solid var(--color-border, #1c2235);
    background: linear-gradient(180deg, rgb(12, 15, 21) 0%, rgb(8, 10, 15) 100%);
    box-shadow: 0 32px 80px rgba(0, 0, 0, 0.7), inset 0 1px 0 rgba(255, 255, 255, 0.03);
    animation: slide-in-right 0.28s cubic-bezier(0.25, 0, 0.25, 1);
  }

  .lightbox-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    flex-shrink: 0;
    padding: 0.55rem 0.85rem;
    border-bottom: 1px solid var(--color-border, #1c2235);
    background: rgba(12, 15, 21, 0.97);
  }
  .lightbox-header h3 {
    margin: 0;
    font-size: 0.72rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--color-text-primary, #f2eed8);
  }

  .lightbox-focus {
    flex: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    min-height: 0;
    padding: 1rem;
    overflow: hidden;
  }
  .lightbox-focus img {
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
  }

  .lightbox-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.5rem;
    color: var(--color-text-disabled, #4a5260);
    font-size: 0.75rem;
  }

  .lightbox-strip {
    display: flex;
    gap: 0.4rem;
    flex-shrink: 0;
    padding: 0.65rem 0.85rem;
    border-top: 1px solid var(--color-border, #1c2235);
    background: rgba(10, 12, 17, 0.6);
    overflow-x: auto;
    scrollbar-width: thin;
    scrollbar-color: rgba(196, 154, 90, 0.15) transparent;
  }

  .lightbox-thumb {
    position: relative;
    flex-shrink: 0;
    width: 4.5rem;
    border: 2px solid transparent;
    background: var(--color-surface-2, #101420);
    padding: 0;
    transition: border-color 0.15s, box-shadow 0.15s;
  }
  .lightbox-thumb:hover {
    border-color: rgba(255, 255, 255, 0.12);
  }
  .lightbox-thumb.active {
    border-color: rgba(196, 154, 90, 0.7);
    box-shadow: 0 0 0 1px rgba(196, 154, 90, 0.35), 0 0 12px rgba(196, 154, 90, 0.15);
  }
  .lightbox-thumb img {
    display: block;
    width: 100%;
    object-fit: cover;
  }
  .lightbox-thumb img[data-aspect="poster"] { aspect-ratio: 2 / 3; }
  .lightbox-thumb img[data-aspect="wide"] { aspect-ratio: 16 / 9; }

  .thumb-check {
    position: absolute;
    top: 0.2rem;
    right: 0.2rem;
    display: grid;
    width: 1rem;
    height: 1rem;
    place-items: center;
    background: rgba(196, 154, 90, 0.9);
    color: rgb(12, 15, 21);
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4);
  }

  /* === Desktop layout === */
  @media (min-width: 900px) {
    .identify-modal {
      padding: clamp(0.75rem, 2vw, 1.5rem);
    }
    .modal-panel {
      width: min(100%, 64rem);
      max-width: calc(100vw - clamp(1.5rem, 4vw, 3rem));
      height: calc(100dvh - clamp(1.5rem, 4vw, 3rem));
      margin: auto;
      max-height: none;
    }
    .lightbox-panel {
      width: min(100%, 64rem);
      max-width: calc(100vw - clamp(1.5rem, 4vw, 3rem));
      height: calc(100dvh - clamp(1.5rem, 4vw, 3rem));
      margin: auto;
      max-height: none;
    }
  }

  /* === Mobile layout === */
  @media (max-width: 899px) {
    .modal-panel {
      min-height: 100dvh;
      border: none;
    }
    .modal-toolbar {
      flex-wrap: wrap;
    }
    .provider-cards {
      overflow-x: auto;
      flex-wrap: nowrap;
      padding-bottom: 0.2rem;
    }
    .provider-card {
      flex-shrink: 0;
    }
    .modal-footer {
      flex-direction: column;
      align-items: stretch;
      gap: 0.4rem;
    }
    .footer-actions {
      flex-direction: column;
    }
    .btn-ghost, .btn-secondary, .btn-primary {
      width: 100%;
    }
    .field-row {
      grid-template-columns: auto minmax(0, 1fr);
      align-items: start;
    }
    .field-arrow {
      display: none;
    }
    .field-new-value {
      grid-column: 2 / -1;
      white-space: normal;
    }
    .art-grid {
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 6.5rem), 8rem));
    }
    .lightbox-panel {
      min-height: 100dvh;
      border: none;
    }
    .lightbox-focus {
      padding: 0.5rem;
    }
    .lightbox-thumb {
      width: clamp(3rem, 16vw, 3.5rem);
    }
  }

  @media (max-width: 520px) {
    .modal-body {
      padding: 0.55rem;
    }
    .section-body {
      padding-inline: 0.55rem;
    }
  }
</style>
