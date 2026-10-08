const palette = [
  "bg-brand-100 text-brand-800",
  "bg-sky-100 text-sky-800",
  "bg-amber-100 text-amber-800",
  "bg-violet-100 text-violet-800",
  "bg-rose-100 text-rose-800",
  "bg-teal-100 text-teal-800",
  "bg-orange-100 text-orange-800",
  "bg-indigo-100 text-indigo-800",
];

export function initials(name: string) {
  const parts = name.replace(/\(.*?\)/g, " ").trim().split(/\s+/).map((p) => p.replace(/[^\p{L}\p{N}]/gu, "")).filter(Boolean);
  if (parts.length === 0) return "?";
  return ((parts[0][0] ?? "") + (parts.length > 1 ? parts[parts.length - 1][0] : "")).toUpperCase();
}

function colorFor(seed: string) {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) | 0;
  return palette[Math.abs(hash) % palette.length];
}

const sizes = { xs: "size-5 text-[9px]", sm: "size-7 text-[11px]", md: "size-9 text-xs", lg: "size-12 text-base" } as const;

/** Avatar com iniciais e cor estável derivada do nome (sem foto — nada de dados extras). */
export function Avatar({ name, size = "sm", className = "" }: { name: string; size?: keyof typeof sizes; className?: string }) {
  return (
    <span
      aria-hidden
      title={name}
      className={`inline-flex shrink-0 items-center justify-center rounded-full font-semibold ring-2 ring-white ${sizes[size]} ${colorFor(name)} ${className}`}
    >
      {initials(name)}
    </span>
  );
}
