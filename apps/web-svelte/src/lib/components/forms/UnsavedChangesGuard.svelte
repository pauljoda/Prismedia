<script lang="ts">
  import { beforeNavigate } from "$app/navigation";

  interface Props {
    /** True only while the mounted editor has changes that have not been saved. */
    dirty: boolean;
  }

  let { dirty }: Props = $props();

  beforeNavigate((navigation) => {
    if (!dirty) return;

    // Refresh and tab close must use the browser's own unload confirmation.
    if (navigation.type === "leave") {
      navigation.cancel();
      return;
    }

    const from = navigation.from?.url;
    const to = navigation.to?.url;
    if (!navigation.willUnload && from && to && from.origin === to.origin && from.pathname === to.pathname && from.search === to.search) return;

    // Keep the original navigation, including back/forward direction, intact.
    // Replaying a cancelled navigation with goto would change browser history.
    if (!window.confirm("Discard unsaved changes and leave this page?")) navigation.cancel();
  });
</script>
