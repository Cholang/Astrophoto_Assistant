import { useEffect, useRef, useState, type FormEvent } from 'react'
import Button from '../components/Button'
import ProfileAvatar from '../components/ProfileAvatar'
import TextField from '../components/TextField'
import { createProfile, MEMO_MAX, NICKNAME_MAX, toSquareProfileImage, uploadProfileImage, type Profile } from '../profiles'
import styles from './NewProfileScreen.module.css'

/**
 * 새 프로필 만들기: 닉네임(필수), 메모(선택), 프로필 이미지(선택, PC의 이미지 파일).
 * 만들면 선택 화면을 거치지 않고 바로 그 프로필로 다음 단계로 간다.
 */
export default function NewProfileScreen({
  firstTime,
  onCreated,
  onCancel,
}: {
  firstTime: boolean
  onCreated: (p: Profile) => void
  onCancel?: () => void
}) {
  const [nickname, setNickname] = useState('')
  const [memo, setMemo] = useState('')
  const [image, setImage] = useState<Blob | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)

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
    }
  }

  const clearImage = () => {
    setImage(null)
    setPreview(null)
    if (fileInput.current) fileInput.current.value = ''
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!nickname.trim() || saving) return
    setSaving(true)
    setError(null)
    try {
      const profile = await createProfile(nickname, memo)
      if (image) await uploadProfileImage(profile.id, image)
      onCreated({ ...profile, hasImage: !!image })
    } catch (err) {
      setError((err as Error).message)
      setSaving(false)
    }
  }

  return (
    <main className={styles.stage}>
      <form className={styles.form} onSubmit={submit} noValidate>
        <div className={styles.content}>
          <div className={styles.intro}>
            <h1>{firstTime ? 'AA를 처음 쓰시는군요' : '새 프로필을 만들어요'}</h1>
            <p>{firstTime ? '이 PC에서 쓸 프로필을 만들어 주세요. 닉네임만 있으면 됩니다.' : '닉네임만 있으면 됩니다. 메모와 이미지는 나중에 넣어도 됩니다.'}</p>
          </div>

          <div className={styles.fields}>
            <div className={styles.imageField}>
              <ProfileAvatar src={preview} name={nickname || '?'} size="large" />
              <div className={styles.imageActions}>
                <span className={styles.imageLabel}>
                  프로필 이미지<span className={styles.optional}>선택</span>
                </span>
                <p className={styles.imageHelp}>사진의 가운데를 정사각형으로 맞춰 넣습니다.</p>
                <div className={styles.imageButtons}>
                  <Button onClick={() => fileInput.current?.click()}>{preview ? '다른 이미지 고르기' : '이미지 고르기'}</Button>
                  {preview && <Button onClick={clearImage}>이미지 빼기</Button>}
                </div>
                <input
                  ref={fileInput}
                  type="file"
                  accept="image/jpeg,image/png,image/webp,image/gif,image/bmp"
                  hidden
                  onChange={(e) => pickImage(e.target.files?.[0])}
                />
              </div>
            </div>

            <TextField
              id="nickname"
              label="닉네임"
              maxLength={NICKNAME_MAX}
              value={nickname}
              onChange={(e) => setNickname(e.target.value)}
              autoFocus
              autoComplete="off"
              required
            />
            <TextField
              id="memo"
              label="메모"
              optional
              maxLength={MEMO_MAX}
              value={memo}
              onChange={(e) => setMemo(e.target.value)}
              placeholder="예: 주말 원정용, 베란다 촬영"
              autoComplete="off"
            />

            {error && (
              <p className={styles.error} role="alert">
                {error}
              </p>
            )}
          </div>
        </div>

        <footer className={styles.footer}>
          {onCancel && <Button onClick={onCancel}>취소</Button>}
          <Button variant="primary" type="submit" disabled={!nickname.trim() || saving}>
            {saving ? '만드는 중' : '만들기'}
          </Button>
        </footer>
      </form>
    </main>
  )
}
