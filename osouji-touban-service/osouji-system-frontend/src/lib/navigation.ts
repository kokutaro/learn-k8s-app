type PreserveScrollNavigateOptions<TSearch> = {
  search: (previous: TSearch) => TSearch
}

export function preserveScrollNavigateOptions<TSearch>(options: PreserveScrollNavigateOptions<TSearch>) {
  return {
    ...options,
    resetScroll: false as const,
  }
}