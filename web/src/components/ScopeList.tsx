import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import styles from './ScopeList.module.css'

/** 서버 OpticsStore.Scope (ScopeView) */
export interface Scope {
  id: string
  name: string
  focalLength: number
  aperture: number | null
  focalRatio: number | null
  reducer: number | null
  flattener: number | null
  optics: string
}

interface Form {
  name: string
  focalLength: string
  aperture: string
  focalRatio: string
  reducer: string
  flattener: string
}

const EMPTY: Form = { name: '', focalLength: '', aperture: '', focalRatio: '', reducer: '', flattener: '' }
const num = (s: string) => (s.trim() === '' ? null : Number(s.replace(',', '.')))
const text = (n: number | null) => (n == null ? '' : String(n))

/**
 * 망원경 목록 (DESIGN.md 3장 "망원경"): 장비 변경 모드에서 망원경 원을 누르면 원 옆에 열린다.
 * 누르면 그 망원경으로(onPick), 연필로 고치기, 휴지통으로 지우기(지금 망원경은 불가), 맨 아래 "망원경 추가".
 * 입력: 이름 · 초점거리(mm) · 구경(mm) 또는 F값 · 리듀서·플래트너 배율(선택). 망원경 정보는 표준 데이터가 없어 모두 직접 입력한다.
 */
export default function ScopeList({
  left,
  onPick,
  onClose,
}: {
  left: boolean
  onPick: (scope: { id: string; name: string }) => void
  onClose: () => void
}) {
  const [data, setData] = useState<{ scopes: Scope[]; currentId: string | null; max: number } | null>(null)
  const [editing, setEditing] = useState<{ id: string | null; form: Form } | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    const res = await fetch('/api/optics')
    if (res.ok) setData(await res.json())
  }, [])
  useEffect(() => {
    void load()
  }, [load])

  // Esc로 닫기 (입력 중이면 입력만 닫기)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      if (editing) setEditing(null)
      else onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [editing, onClose])

  const save = async () => {
    if (!editing) return
    const f = editing.form
    setError(null)
    const body = {
      name: f.name,
      focalLength: num(f.focalLength) ?? 0,
      aperture: num(f.aperture),
      focalRatio: num(f.focalRatio),
      reducer: num(f.reducer),
      flattener: num(f.flattener),
    }
    const res = await fetch(editing.id ? `/api/optics/${editing.id}` : '/api/optics', {
      method: editing.id ? 'PUT' : 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    const result = await res.json().catch(() => ({}))
    if (!res.ok) return setError(result.error ?? '저장하지 못했습니다')
    setEditing(null)
    await load()
    // 새로 추가했거나 지금 망원경을 고쳤으면 그 망원경으로 (칸의 이름·초점거리를 바꾼다)
    if (!editing.id || editing.id === data?.currentId) onPick({ id: result.id, name: result.name })
  }

  const remove = async (s: Scope) => {
    setError(null)
    const res = await fetch(`/api/optics/${s.id}`, { method: 'DELETE' })
    if (!res.ok) return setError((await res.json().catch(() => ({}))).error ?? '지우지 못했습니다')
    await load()
  }

  const set = (key: keyof Form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setEditing((ed) => (ed ? { ...ed, form: { ...ed.form, [key]: e.target.value } } : ed))

  return (
    <div className={styles.panel} data-left={left} role="dialog" aria-label="망원경 목록">
      {editing ? (
        <form
          className={styles.form}
          onSubmit={(e) => {
            e.preventDefault()
            void save()
          }}
        >
          <b className={styles.formTitle}>{editing.id ? '망원경 고치기' : '망원경 추가'}</b>
          <label>
            이름
            <input value={editing.form.name} onChange={set('name')} placeholder="예: Pleiades 68" autoFocus />
          </label>
          <label>
            초점거리 (mm)
            <input value={editing.form.focalLength} onChange={set('focalLength')} inputMode="decimal" placeholder="예: 260" />
          </label>
          <div className={styles.pair}>
            <label>
              구경 (mm)
              <input value={editing.form.aperture} onChange={set('aperture')} inputMode="decimal" placeholder="둘 중 하나" />
            </label>
            <label>
              F값
              <input value={editing.form.focalRatio} onChange={set('focalRatio')} inputMode="decimal" placeholder="예: 3.8" />
            </label>
          </div>
          <div className={styles.pair}>
            <label>
              리듀서 배율 (선택)
              <input value={editing.form.reducer} onChange={set('reducer')} inputMode="decimal" placeholder="예: 0.8" />
            </label>
            <label>
              플래트너 배율 (선택)
              <input value={editing.form.flattener} onChange={set('flattener')} inputMode="decimal" placeholder="예: 1.0" />
            </label>
          </div>
          <p className={styles.error} role="alert">
            {error}
          </p>
          <div className={styles.actions}>
            <button type="button" className={styles.quiet} onClick={() => setEditing(null)}>
              취소
            </button>
            <button type="submit" className={styles.primary}>
              {editing.id ? '고치기' : '추가하기'}
            </button>
          </div>
        </form>
      ) : (
        <>
          {data === null && <p className={styles.note}>망원경 목록을 읽는 중입니다</p>}
          {data?.scopes.length === 0 && <p className={styles.note}>등록된 망원경이 없습니다. 쓰는 망원경을 추가해 주세요.</p>}
          <ul className={styles.list}>
            {data?.scopes.map((s) => {
              const current = s.id === data.currentId
              return (
                <li key={s.id} className={styles.row} data-current={current}>
                  <button type="button" className={styles.pick} aria-current={current || undefined} onClick={() => onPick({ id: s.id, name: s.name })}>
                    <span className={styles.name}>{s.name}</span>
                    <span className={styles.optics}>{s.optics}</span>
                  </button>
                  <button
                    type="button"
                    className={styles.icon}
                    aria-label={`${s.name} 고치기`}
                    title="고치기"
                    onClick={() =>
                      setEditing({
                        id: s.id,
                        form: {
                          name: s.name,
                          focalLength: text(s.focalLength),
                          aperture: text(s.aperture),
                          focalRatio: text(s.focalRatio),
                          reducer: text(s.reducer),
                          flattener: text(s.flattener),
                        },
                      })
                    }
                  >
                    <Pencil strokeWidth={2} aria-hidden="true" />
                  </button>
                  <button
                    type="button"
                    className={styles.icon}
                    aria-label={`${s.name} 지우기`}
                    title={current ? '지금 쓰는 망원경은 지울 수 없습니다' : '지우기'}
                    disabled={current}
                    onClick={() => remove(s)}
                  >
                    <Trash2 strokeWidth={2} aria-hidden="true" />
                  </button>
                </li>
              )
            })}
          </ul>
          <p className={styles.error} role="alert">
            {error}
          </p>
          {data && data.scopes.length < data.max && (
            <button type="button" className={styles.add} onClick={() => setEditing({ id: null, form: EMPTY })}>
              <Plus strokeWidth={2} aria-hidden="true" />
              망원경 추가
            </button>
          )}
        </>
      )}
    </div>
  )
}
