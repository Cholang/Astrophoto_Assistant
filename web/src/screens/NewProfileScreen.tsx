import { Activity, Pencil } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import Button from '../components/Button'
import PowerWiringPanel, { wiringSummary } from '../components/PowerWiringPanel'
import ProfileAvatar from '../components/ProfileAvatar'
import TextField from '../components/TextField'
import { createProfile, DEFAULT_WIRING, editProfile, getPower, MEMO_MAX_BYTES, NICKNAME_MAX_BYTES, profileImageUrl, savePower, toSquareProfileImage, uploadProfileImage, type PowerWiring, type Profile } from '../profiles'
import styles from './NewProfileScreen.module.css'

/**
 * 새 프로필 만들기 (화면 가운데 한 줄 배치):
 * 프로필 이미지 → [이미지 변경] → 닉네임(필수) → 메모(선택) → [프로필 생성].
 * 만들면 선택 화면을 거치지 않고 바로 그 프로필로 다음 단계로 간다.
 */
export default function NewProfileScreen({
  editing = null,
  onCreated,
  onEdited,
  onCancel,
}: {
  /** 있으면 새로 만들지 않고 이 프로필을 고친다 (프로필 선택 화면의 연필) */
  editing?: Profile | null
  onCreated: (p: Profile) => void
  onEdited?: (p: Profile) => void
  onCancel?: () => void
}) {
  const [nickname, setNickname] = useState(editing?.nickname ?? '')
  const [memo, setMemo] = useState(editing?.memo ?? '')
  const [image, setImage] = useState<Blob | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const shownImage = preview ?? (editing?.hasImage ? profileImageUrl(editing) : null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  // 전원 배선 (DESIGN.md 3장): 오른쪽 패널에서 고치고 프로필과 함께 저장. 손대지 않으면(custom=false) 저장하지 않아 기본값(적도의 먼저)을 쓴다
  const [wiring, setWiring] = useState<PowerWiring>(DEFAULT_WIRING)
  const [custom, setCustom] = useState(false)
  const [changed, setChanged] = useState(false)
  const [wiringOpen, setWiringOpen] = useState(false)
  useEffect(() => {
    if (!editing) return
    getPower(editing.id)
      .then((p) => {
        setWiring(p.sources)
        setCustom(p.custom)
      })
      .catch(() => null)
  }, [editing])

  useEffect(() => () => {
    if (preview) URL.revokeObjectURL(preview)
  }, [preview])

  const pickImage = async (file: File | undefined) => {
    if (!file) return
    setError(null)
    try {
      const square = await toSquareProfileImage(file)
      setImage(square)
      setPreview(URL.createObjectURL(square))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!nickname.trim() || saving) return
    setSaving(true)
    setError(null)
    try {
      if (editing) {
        const profile = await editProfile(editing.id, nickname, memo)
        if (image) await uploadProfileImage(profile.id, image)
        if (changed) await savePower(profile.id, wiring)
        // 이미지 주소가 바뀌도록 시각을 갱신 (profileImageUrl이 lastUsedAt을 쓴다)
        onEdited?.({ ...profile, hasImage: profile.hasImage || !!image, lastUsedAt: image ? new Date().toISOString() : profile.lastUsedAt })
        return
      }
      const profile = await createProfile(nickname, memo)
      if (image) await uploadProfileImage(profile.id, image)
      if (changed) await savePower(profile.id, wiring)
      onCreated({ ...profile, hasImage: !!image })
    } catch (err) {
      setError((err as Error).message)
      setSaving(false)
    }
  }

  return (
    <main className={styles.stage} data-wiring={wiringOpen}>
      <form className={styles.form} onSubmit={submit} noValidate>
        <div className={styles.head}>
          <h1 className={styles.title}>{editing ? '프로필 편집' : '프로필 선택'}</h1>

          <div className={styles.imageSlot}>
            <ProfileAvatar src={shownImage} size="hero" />
            {/* 이미지 변경: 이미지 오른쪽 아래의 정사각형 연필 버튼 (이미지 한 변의 약 1/6) */}
            <button type="button" className={styles.editImage} aria-label="이미지 변경" title="이미지 변경" onClick={() => fileInput.current?.click()}>
              <Pencil strokeWidth={2} aria-hidden="true" />
            </button>
          </div>
        </div>
        <div hidden>
          <input
            ref={fileInput}
            type="file"
            accept="image/jpeg,image/png,image/webp,image/gif,image/bmp"
            hidden
            onChange={(e) => pickImage(e.target.files?.[0])}
          />
        </div>

        <div className={styles.fields}>
          <TextField
            id="nickname"
            label="별명"
            maxBytes={NICKNAME_MAX_BYTES}
            placeholder={`최대 ${NICKNAME_MAX_BYTES}byte`}
            value={nickname}
            onValueChange={setNickname}
            autoFocus
            autoComplete="off"
            required
          />
          <TextField
            id="memo"
            label="메모"
            maxBytes={MEMO_MAX_BYTES}
            placeholder={`(선택) 최대 ${MEMO_MAX_BYTES}byte`}
            value={memo}
            onValueChange={setMemo}
            autoComplete="off"
          />
        </div>

        {/* 전원 배선: 자주 바꾸지 않는 설정이라 프로필에서 (2026-10-10 사용자 결정). 누르면 오른쪽에 패널 */}
        <button type="button" className={styles.wiring} aria-expanded={wiringOpen} onClick={() => setWiringOpen((v) => !v)}>
          <Activity aria-hidden="true" />
          <span>
            <b>전원 배선</b>
            <small>{custom || changed ? wiringSummary(wiring) : `기본 · ${wiringSummary(DEFAULT_WIRING)}`}</small>
          </span>
        </button>

        {error && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}

        <div className={styles.actions}>
          <Button variant="primary" type="submit" disabled={!nickname.trim() || saving}>
            {editing ? (saving ? '저장 중' : '저장') : saving ? '생성 중' : '프로필 생성'}
          </Button>
          {onCancel && (
            <button type="button" className={styles.back} onClick={onCancel}>
              프로필 목록으로 돌아가기
            </button>
          )}
        </div>
      </form>
      <aside className={styles.side} aria-hidden={!wiringOpen} inert={!wiringOpen}>
        <PowerWiringPanel
          value={wiring}
          onChange={(next) => {
            setWiring(next)
            setChanged(true)
          }}
          onClose={() => setWiringOpen(false)}
        />
      </aside>
    </main>
  )
}
