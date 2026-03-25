import { useState, useEffect, useMemo } from 'react';
import { getLibrary, refreshLibrary, getPosterUrl, LibraryItem } from '../../api/client';
import { formatSize } from '../../utils/format';
import MediaModal from './MediaModal';

interface Props {
  addToast: (msg: string, type: 'error' | 'info') => void;
  onPlay: () => void;
}

type TypeFilter = 'all' | 'movie' | 'tv';

export default function LibraryTab({ addToast, onPlay }: Props) {
  const [items, setItems] = useState<LibraryItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [typeFilter, setTypeFilter] = useState<TypeFilter>('all');
  const [search, setSearch] = useState('');
  const [selectedItem, setSelectedItem] = useState<LibraryItem | null>(null);

  useEffect(() => {
    let mounted = true;
    const fetchLibrary = async () => {
      try {
        setLoading(true);
        const data = await getLibrary();
        if (mounted) setItems(data.items);
      } catch (err) {
        if (mounted) addToast(err instanceof Error ? err.message : 'Failed to load library', 'error');
      } finally {
        if (mounted) setLoading(false);
      }
    };
    fetchLibrary();
    return () => { mounted = false; };
  }, [addToast]);

  const filteredItems = useMemo(() => {
    let result = items;
    if (typeFilter !== 'all') {
      result = result.filter((item) => item.type === typeFilter);
    }
    if (search.trim()) {
      const q = search.trim().toLowerCase();
      result = result.filter((item) => item.title.toLowerCase().includes(q));
    }
    return result;
  }, [items, typeFilter, search]);

  const handleRefresh = async () => {
    try {
      setRefreshing(true);
      await refreshLibrary();
      const data = await getLibrary();
      setItems(data.items);
      addToast('Library refreshed', 'info');
    } catch (err) {
      addToast(err instanceof Error ? err.message : 'Failed to refresh library', 'error');
    } finally {
      setRefreshing(false);
    }
  };

  const filterButtons: { label: string; value: TypeFilter }[] = [
    { label: 'All', value: 'all' },
    { label: 'Movies', value: 'movie' },
    { label: 'TV', value: 'tv' },
  ];

  return (
    <div>
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-3 mb-5">
        <input
          type="text"
          placeholder="Search library..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="flex-1 min-w-[200px] px-3.5 py-2 bg-surface-2 border border-border rounded-lg text-text text-sm outline-none focus:border-accent placeholder:text-text-dim"
        />
        <div className="flex gap-1">
          {filterButtons.map((btn) => (
            <button
              key={btn.value}
              onClick={() => setTypeFilter(btn.value)}
              className={`px-3.5 py-1.5 rounded-md text-xs font-medium border transition-colors ${
                typeFilter === btn.value
                  ? 'bg-accent border-accent text-white'
                  : 'bg-surface-2 border-border text-text-dim hover:border-accent hover:text-text'
              }`}
            >
              {btn.label}
            </button>
          ))}
        </div>
        <button
          onClick={handleRefresh}
          disabled={refreshing}
          className="flex items-center gap-1.5 px-3.5 py-1.5 bg-surface-2 border border-border rounded-md text-xs text-text-dim hover:border-accent hover:text-text disabled:opacity-50 transition-colors"
        >
          {refreshing ? (
            <svg className="w-3.5 h-3.5 animate-spin" viewBox="0 0 24 24" fill="none">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
          ) : (
            <span className="text-sm">&#x21bb;</span>
          )}
          Refresh
        </button>
        <span className="ml-auto text-xs text-text-dim">
          {filteredItems.length} item{filteredItems.length !== 1 ? 's' : ''}
        </span>
      </div>

      {/* Loading */}
      {loading && (
        <div className="text-center py-16 text-text-dim text-sm">
          <svg className="w-5 h-5 animate-spin mx-auto mb-2 text-accent" viewBox="0 0 24 24" fill="none">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
          </svg>
          Loading library...
        </div>
      )}

      {/* Empty state */}
      {!loading && filteredItems.length === 0 && (
        <div className="text-center py-16 text-text-dim text-sm">
          <div className="text-4xl mb-3 opacity-30">&#128218;</div>
          <p>No items found</p>
          {search && (
            <p className="mt-1 text-xs">Try adjusting your search or filter</p>
          )}
        </div>
      )}

      {/* Grid */}
      {!loading && filteredItems.length > 0 && (
        <div className="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-4">
          {filteredItems.map((item) => (
            <div
              key={item.id}
              onClick={() => setSelectedItem(item)}
              className="bg-surface border border-border rounded-[10px] overflow-hidden cursor-pointer transition-all hover:border-accent hover:-translate-y-0.5"
            >
              <PosterCard item={item} />
              <div className="px-3 py-2.5">
                <div className="text-[13px] font-medium truncate mb-1">{item.title}</div>
                <div className="text-[11px] text-text-dim flex justify-between">
                  <span>{item.year ?? '—'}</span>
                  <span>{formatSize(item.totalSize)}</span>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Modal */}
      {selectedItem && (
        <MediaModal
          item={selectedItem}
          onClose={() => setSelectedItem(null)}
          onPlay={onPlay}
        />
      )}
    </div>
  );
}

function PosterCard({ item }: { item: LibraryItem }) {
  const [imgError, setImgError] = useState(false);
  const letter = item.title.charAt(0).toUpperCase();
  const isTV = item.type === 'tv';

  return (
    <div className="w-full aspect-[2/3] bg-surface-2 relative overflow-hidden">
      {!imgError ? (
        <img
          src={getPosterUrl(item.id)}
          alt={item.title}
          className="w-full h-full object-cover"
          onError={() => setImgError(true)}
          loading="lazy"
        />
      ) : (
        <div className="w-full h-full flex items-center justify-center bg-gradient-to-br from-surface-2 to-surface text-[56px] font-bold text-text-dim/20">
          {letter}
        </div>
      )}
      <span
        className={`absolute top-2 right-2 px-1.5 py-0.5 rounded text-[10px] font-semibold uppercase ${
          isTV ? 'bg-blue/80 text-white' : 'bg-accent/80 text-white'
        }`}
      >
        {isTV ? 'TV' : 'Movie'}
      </span>
    </div>
  );
}
