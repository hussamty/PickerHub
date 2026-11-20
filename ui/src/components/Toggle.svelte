<script lang="ts">
  import { t } from "../lib/i18n";
  let { label, hint = "", checked, onchange }: { label: string; hint?: string; checked: boolean; onchange: (v: boolean) => void } = $props();
</script>

<label class="toggle">
  <span class="text">
    <span>{t(label)}</span>
    {#if hint}<small class="muted">{t(hint)}</small>{/if}
  </span>
  <input type="checkbox" role="switch" {checked} onchange={(e) => onchange((e.currentTarget as HTMLInputElement).checked)} />
  <span class="track" aria-hidden="true"><span class="thumb"></span></span>
</label>

<style>
  .toggle {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    min-height: 44px;
    cursor: pointer;
  }
  .text {
    display: flex;
    flex-direction: column;
  }
  small {
    font-size: var(--text-label);
    line-height: 1.6;
  }
  input {
    position: absolute;
    opacity: 0;
    pointer-events: none;
  }
  .track {
    position: relative;
    flex: none;
    width: 44px;
    height: 26px;
    border-radius: 999px;
    background: var(--tint-strong);
    border: 1px solid var(--line);
    transition: background 0.2s;
  }
  .thumb {
    position: absolute;
    top: 3px;
    inset-inline-start: 3px;
    width: 18px;
    height: 18px;
    border-radius: 999px;
    background: var(--ink);
    transition: inset-inline-start 0.22s var(--ease-spring), background 0.2s;
  }
  input:checked + .track {
    background: var(--color-brand);
  }
  input:checked + .track .thumb {
    inset-inline-start: 21px;
    background: #111;
  }
  input:focus-visible + .track {
    outline: 2px solid var(--color-brand);
    outline-offset: 2px;
  }
</style>
