<script lang="ts">
  import { onMount, onDestroy, type Component } from "svelte";
  import { Editor } from "@tiptap/core";
  import StarterKit from "@tiptap/starter-kit";
  import Link from "@tiptap/extension-link";
  import Placeholder from "@tiptap/extension-placeholder";
  import { Markdown } from "tiptap-markdown";
  import {
    Bold,
    Code,
    Heading2,
    Italic,
    Link as LinkIcon,
    List,
    ListOrdered,
    Quote,
    Redo2,
    Strikethrough,
    Undo2,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";

  interface Props {
    value: string;
    onChange: (value: string) => void;
    label?: string;
    icon?: Component;
    placeholder?: string;
    helper?: string;
    error?: string;
    disabled?: boolean;
    minHeight?: string;
  }

  let {
    value,
    onChange,
    label,
    icon,
    placeholder = "Write something…",
    helper,
    error,
    disabled = false,
    minHeight = "8rem",
  }: Props = $props();

  let editorElement: HTMLDivElement | null = $state(null);
  let editor: Editor | null = $state(null);
  let focused = $state(false);

  const id = `md-${Math.random().toString(36).slice(2, 9)}`;

  onMount(() => {
    if (!editorElement) return;

    editor = new Editor({
      element: editorElement,
      extensions: [
        StarterKit.configure({
          heading: { levels: [2, 3] },
        }),
        Link.configure({
          openOnClick: false,
          HTMLAttributes: { class: "editor-link" },
        }),
        Placeholder.configure({ placeholder }),
        Markdown.configure({
          html: false,
          transformPastedText: true,
          transformCopiedText: true,
        }),
      ],
      content: value,
      editable: !disabled,
      onUpdate: ({ editor: e }) => {
        onChange((e.storage as Record<string, any>).markdown.getMarkdown());
      },
      onFocus: () => (focused = true),
      onBlur: () => (focused = false),
    });
  });

  $effect(() => {
    if (!editor) return;
    const current = (editor.storage as Record<string, any>).markdown.getMarkdown();
    if (current !== value) {
      editor.commands.setContent(value);
    }
  });

  $effect(() => {
    if (editor) editor.setEditable(!disabled);
  });

  onDestroy(() => {
    editor?.destroy();
  });

  interface ToolbarAction {
    Icon: Component;
    label: string;
    command: () => void;
    active: () => boolean;
  }

  function toolbarActions(): ToolbarAction[] {
    if (!editor) return [];
    return [
      {
        Icon: Bold,
        label: "Bold",
        command: () => editor!.chain().focus().toggleBold().run(),
        active: () => editor!.isActive("bold"),
      },
      {
        Icon: Italic,
        label: "Italic",
        command: () => editor!.chain().focus().toggleItalic().run(),
        active: () => editor!.isActive("italic"),
      },
      {
        Icon: Strikethrough,
        label: "Strikethrough",
        command: () => editor!.chain().focus().toggleStrike().run(),
        active: () => editor!.isActive("strike"),
      },
      {
        Icon: Heading2,
        label: "Heading",
        command: () => editor!.chain().focus().toggleHeading({ level: 2 }).run(),
        active: () => editor!.isActive("heading", { level: 2 }),
      },
      {
        Icon: List,
        label: "Bullet list",
        command: () => editor!.chain().focus().toggleBulletList().run(),
        active: () => editor!.isActive("bulletList"),
      },
      {
        Icon: ListOrdered,
        label: "Ordered list",
        command: () => editor!.chain().focus().toggleOrderedList().run(),
        active: () => editor!.isActive("orderedList"),
      },
      {
        Icon: Quote,
        label: "Blockquote",
        command: () => editor!.chain().focus().toggleBlockquote().run(),
        active: () => editor!.isActive("blockquote"),
      },
      {
        Icon: Code,
        label: "Code",
        command: () => editor!.chain().focus().toggleCode().run(),
        active: () => editor!.isActive("code"),
      },
      {
        Icon: LinkIcon,
        label: "Link",
        command: () => {
          if (editor!.isActive("link")) {
            editor!.chain().focus().unsetLink().run();
          } else {
            const url = prompt("URL");
            if (url) editor!.chain().focus().setLink({ href: url }).run();
          }
        },
        active: () => editor!.isActive("link"),
      },
    ];
  }
</script>

<FormField {label} {icon} {helper} {error} htmlFor={id}>
  <div
    class={cn(
      "markdown-editor",
      focused && "is-focused",
      disabled && "is-disabled",
      error && "is-error",
    )}
  >
    {#if editor}
      <div class="toolbar" role="toolbar" aria-label="Formatting">
        <div class="toolbar-group">
          {#each toolbarActions() as action (action.label)}
            {@const ActionIcon = action.Icon}
            <button
              type="button"
              class="toolbar-btn"
              class:active={action.active()}
              title={action.label}
              aria-label={action.label}
              aria-pressed={action.active()}
              onmousedown={(e) => {
                e.preventDefault();
                action.command();
              }}
            >
              <ActionIcon class="h-3.5 w-3.5" />
            </button>
          {/each}
        </div>
        <div class="toolbar-group">
          <button
            type="button"
            class="toolbar-btn"
            title="Undo"
            aria-label="Undo"
            disabled={!editor.can().undo()}
            onmousedown={(e) => {
              e.preventDefault();
              editor!.chain().focus().undo().run();
            }}
          >
            <Undo2 class="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            class="toolbar-btn"
            title="Redo"
            aria-label="Redo"
            disabled={!editor.can().redo()}
            onmousedown={(e) => {
              e.preventDefault();
              editor!.chain().focus().redo().run();
            }}
          >
            <Redo2 class="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
    {/if}

    <div {id} class="editor-content" bind:this={editorElement} style:min-height={minHeight}></div>
  </div>
</FormField>

<style>
  .markdown-editor {
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-sm, 6px);
    background: var(--color-surface-2, #11151c);
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30);
    overflow: hidden;
    transition: border-color 0.18s, box-shadow 0.18s;
  }

  .markdown-editor.is-focused {
    border-color: var(--color-border-accent, rgba(199, 155, 92, 0.24));
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30), 0 0 0 1px rgba(242, 194, 106, 0.35), 0 0 8px rgba(242, 194, 106, 0.15);
  }

  .markdown-editor.is-disabled {
    opacity: 0.5;
    pointer-events: none;
  }

  .markdown-editor.is-error {
    border-color: rgba(239, 68, 68, 0.6);
  }

  .toolbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.25rem;
    padding: 0.35rem 0.5rem;
    border-bottom: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    background: var(--color-surface-3, #181d27);
  }

  .toolbar-group {
    display: flex;
    align-items: center;
    gap: 1px;
  }

  .toolbar-btn {
    display: grid;
    place-items: center;
    width: 1.75rem;
    height: 1.75rem;
    padding: 0;
    border: 1px solid transparent;
    border-radius: var(--radius-xs, 4px);
    background: transparent;
    color: var(--color-text-muted, #a4acb9);
    cursor: pointer;
    transition: color 0.15s, background 0.15s, border-color 0.15s, box-shadow 0.15s;
  }

  .toolbar-btn:hover {
    color: var(--color-text-primary, #f5f2ea);
    background: var(--color-surface-4, #1f2533);
  }

  .toolbar-btn.active {
    color: var(--color-text-accent, #c79b5c);
    border-color: rgba(199, 155, 92, 0.24);
    background: rgba(199, 155, 92, 0.08);
    box-shadow: 0 0 8px rgba(199, 155, 92, 0.1);
  }

  .toolbar-btn:disabled {
    opacity: 0.35;
    cursor: not-allowed;
  }

  .editor-content {
    padding: 0.65rem 0.75rem;
    color: var(--color-text-primary, #f5f2ea);
    font-size: 0.86rem;
    line-height: 1.65;
    cursor: text;
  }

  .editor-content :global(.tiptap) {
    outline: none;
    min-height: inherit;
  }

  .editor-content :global(.tiptap p.is-editor-empty:first-child::before) {
    content: attr(data-placeholder);
    float: left;
    height: 0;
    color: var(--color-text-disabled, #5a6070);
    pointer-events: none;
  }

  .editor-content :global(.tiptap p) {
    margin: 0 0 0.5rem;
  }

  .editor-content :global(.tiptap p:last-child) {
    margin-bottom: 0;
  }

  .editor-content :global(.tiptap strong) {
    font-weight: 600;
    color: var(--color-text-primary, #f5f2ea);
  }

  .editor-content :global(.tiptap em) {
    font-style: italic;
  }

  .editor-content :global(.tiptap s) {
    text-decoration: line-through;
    opacity: 0.6;
  }

  .editor-content :global(.tiptap h2) {
    margin: 0.75rem 0 0.35rem;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.05rem;
    font-weight: 600;
    color: var(--color-text-primary, #f5f2ea);
  }

  .editor-content :global(.tiptap h3) {
    margin: 0.65rem 0 0.3rem;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.95rem;
    font-weight: 600;
    color: var(--color-text-primary, #f5f2ea);
  }

  .editor-content :global(.tiptap ul),
  .editor-content :global(.tiptap ol) {
    margin: 0.35rem 0;
    padding-left: 1.4rem;
  }

  .editor-content :global(.tiptap li) {
    margin-bottom: 0.15rem;
  }

  .editor-content :global(.tiptap blockquote) {
    margin: 0.5rem 0;
    padding: 0.35rem 0.75rem;
    border-left: 3px solid rgba(199, 155, 92, 0.35);
    color: var(--color-text-muted, #a4acb9);
    font-style: italic;
  }

  .editor-content :global(.tiptap code) {
    padding: 0.1em 0.3em;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.82em;
    color: var(--color-text-primary, #f5f2ea);
    background: var(--color-surface-3, #181d27);
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: 3px;
  }

  .editor-content :global(.tiptap pre) {
    margin: 0.5rem 0;
    padding: 0.65rem 0.85rem;
    background: var(--color-surface-3, #181d27);
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
    overflow-x: auto;
  }

  .editor-content :global(.tiptap pre code) {
    padding: 0;
    border: none;
    background: none;
  }

  .editor-content :global(.tiptap a),
  .editor-content :global(.editor-link) {
    color: var(--color-text-accent, #c79b5c);
    text-decoration: underline;
    text-decoration-color: rgba(199, 155, 92, 0.35);
    text-underline-offset: 2px;
  }

  .editor-content :global(.tiptap hr) {
    border: none;
    border-top: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    margin: 0.75rem 0;
  }
</style>
