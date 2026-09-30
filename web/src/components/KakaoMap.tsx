import { LocateFixed, Search } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { appConfig } from '../sites'
import styles from './KakaoMap.module.css'

/* eslint-disable @typescript-eslint/no-explicit-any -- 카카오 지도 SDK는 타입 정의가 없다 */
declare global {
  interface Window {
    kakao?: any
  }
}

/** 관측지 확인에 알맞은 확대 단계: 주변 지형이 보이는 정도 (2026-10-01 사용자 결정, 카카오 5단계) */
export const SITE_LEVEL = 5

let loading: Promise<any> | null = null

/** 마지막으로 본 지도 자리와 확대 단계 (앱을 켜 둔 동안). 보여 줄 곳이 없을 때 여기서 시작한다 */
let lastView: { latitude: number; longitude: number; level: number } | null = null
/** 아무 기록도 없을 때: 서울, 축척 250m(카카오 5단계) — 2026-10-01 사용자 결정 */
const SEOUL = { latitude: 37.5665, longitude: 126.978, level: SITE_LEVEL }
/** 현재 위치를 이만큼만 기다린다. 늦게 잡히면(또는 그사이 지도를 움직였으면) 옮기지 않는다 */
const LOCATE_WAIT_MS = 3000

/**
 * 카카오 지도 SDK를 한 번만 불러온다 (장소 검색 포함). 키는 서버 설정 Map:KakaoJavaScriptKey.
 * 인터넷에서 불러오는 유일한 화면 자원 — 시작·계획은 온라인 필수라 허용 (DESIGN.md 1장).
 * 키가 없거나 불러오지 못하면 null.
 */
function loadKakao(): Promise<any> {
  loading ??= appConfig().then(
    ({ kakaoKey }) =>
      new Promise((resolve) => {
        if (!kakaoKey) return resolve(null)
        if (window.kakao?.maps) return window.kakao.maps.load(() => resolve(window.kakao))
        const s = document.createElement('script')
        s.src = `https://dapi.kakao.com/v2/maps/sdk.js?appkey=${encodeURIComponent(kakaoKey)}&libraries=services&autoload=false`
        s.onload = () => (window.kakao?.maps ? window.kakao.maps.load(() => resolve(window.kakao)) : resolve(null))
        s.onerror = () => resolve(null)
        document.head.appendChild(s)
      }),
  )
  return loading
}

export interface Spot {
  latitude: number
  longitude: number
}

/**
 * 카카오맵. 보여 줄 곳(spot)에 핀을 꽂는다.
 * editable이면 지도를 눌러 핀을 옮기고, 장소 검색·현재 위치를 쓸 수 있다 → onPick으로 좌표를 알린다.
 */
export default function KakaoMap({
  spot,
  editable = false,
  locate = false,
  onPick,
}: {
  spot: Spot | null
  editable?: boolean
  /** 처음 열 때 현재 위치로 옮겨 본다 (관측지 추가). 먼저 마지막 본 자리(없으면 서울)로 띄우고, 3초 안에 잡히면 옮긴다 */
  locate?: boolean
  onPick?: (s: Spot) => void
}) {
  const box = useRef<HTMLDivElement>(null)
  const map = useRef<any>(null)
  const marker = useRef<any>(null)
  const pick = useRef(onPick)
  pick.current = onPick
  const spotRef = useRef(spot)
  spotRef.current = spot
  const [state, setState] = useState<'loading' | 'ready' | 'none'>('loading')
  const [query, setQuery] = useState('')
  const [notice, setNotice] = useState<string | null>(null)

  // 지도 만들기 (처음 한 번)
  useEffect(() => {
    let alive = true
    loadKakao().then((kakao) => {
      if (!alive || !box.current) return
      if (!kakao) return setState('none')
      // 시작 자리: 보여 줄 곳 → 마지막 본 자리·확대 → 서울(250m)
      const start = spot ? { ...spot, level: SITE_LEVEL } : lastView ?? SEOUL
      const center = new kakao.maps.LatLng(start.latitude, start.longitude)
      map.current = new kakao.maps.Map(box.current, { center, level: start.level })
      marker.current = new kakao.maps.Marker({ position: center })
      if (spot) marker.current.setMap(map.current)
      // 사용자가 지도를 움직였는지 (현재 위치가 늦게 와도 덮어쓰지 않게) · 마지막 본 자리 기억
      let moved = false
      kakao.maps.event.addListener(map.current, 'dragstart', () => (moved = true))
      kakao.maps.event.addListener(map.current, 'zoom_start', () => (moved = true))
      kakao.maps.event.addListener(map.current, 'idle', () => {
        const c = map.current.getCenter()
        lastView = { latitude: c.getLat(), longitude: c.getLng(), level: map.current.getLevel() }
      })
      if (locate && !spot && navigator.geolocation)
        navigator.geolocation.getCurrentPosition(
          (p) => {
            if (!alive || moved || spotRef.current) return
            map.current.setLevel(SITE_LEVEL)
            map.current.setCenter(new kakao.maps.LatLng(p.coords.latitude, p.coords.longitude))
          },
          () => undefined,
          { timeout: LOCATE_WAIT_MS, maximumAge: 600_000 },
        )
      kakao.maps.event.addListener(map.current, 'click', (e: any) => {
        if (!editableRef.current) return
        marker.current.setPosition(e.latLng)
        marker.current.setMap(map.current)
        pick.current?.({ latitude: e.latLng.getLat(), longitude: e.latLng.getLng() })
      })
      setState('ready')
    })
    // 창 크기·배율이 바뀌면 지도에 알린다 (타일을 다시 그림)
    const ro = new ResizeObserver(() => map.current?.relayout())
    if (box.current) ro.observe(box.current)
    return () => {
      alive = false
      ro.disconnect()
    }
    // 처음 한 번만. 이후 spot 변화는 아래에서 옮긴다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const editableRef = useRef(editable)
  editableRef.current = editable

  // 보여 줄 곳이 바뀌면 옮긴다
  useEffect(() => {
    const kakao = window.kakao
    if (state !== 'ready' || !kakao) return
    if (!spot) {
      marker.current.setMap(null)
      return
    }
    const p = new kakao.maps.LatLng(spot.latitude, spot.longitude)
    marker.current.setPosition(p)
    marker.current.setMap(map.current)
    map.current.setCenter(p)
  }, [spot?.latitude, spot?.longitude, state]) // eslint-disable-line react-hooks/exhaustive-deps

  // 편집을 시작하면 확대 단계를 맞춘다
  useEffect(() => {
    if (state === 'ready' && editable && spot) map.current.setLevel(SITE_LEVEL)
  }, [editable, state]) // eslint-disable-line react-hooks/exhaustive-deps

  const search = () => {
    const kakao = window.kakao
    if (!kakao || !query.trim()) return
    new kakao.maps.services.Places().keywordSearch(query.trim(), (data: any[], status: string) => {
      if (status !== kakao.maps.services.Status.OK || !data.length) return setNotice('찾는 장소가 없습니다')
      setNotice(null)
      const first = data[0]
      const s = { latitude: +first.y, longitude: +first.x }
      map.current.setLevel(SITE_LEVEL)
      pick.current?.(s)
    })
  }

  const here = () => {
    if (!navigator.geolocation) return setNotice('이 PC에서는 현재 위치를 알 수 없습니다')
    setNotice('현재 위치를 찾는 중입니다')
    navigator.geolocation.getCurrentPosition(
      (p) => {
        setNotice(null)
        pick.current?.({ latitude: p.coords.latitude, longitude: p.coords.longitude })
      },
      () => setNotice('현재 위치를 알 수 없습니다. 윈도우 설정에서 위치 서비스를 켜거나 지도에서 직접 골라 주세요'),
      { enableHighAccuracy: true, timeout: 10000 },
    )
  }

  return (
    <div className={styles.wrap} data-editable={editable}>
      <div ref={box} className={styles.map} />
      {state === 'none' && (
        <p className={styles.none}>
          지도를 불러오지 못했습니다. 인터넷 연결과 카카오맵 키(README "설정")를 확인해 주세요. 좌표는 붙여 넣어 입력할 수 있습니다.
        </p>
      )}
      {editable && state === 'ready' && (
        <>
          <form
            className={styles.search}
            onSubmit={(e) => {
              e.preventDefault()
              search()
            }}
          >
            <Search strokeWidth={2} aria-hidden="true" />
            <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="장소 검색 (예: 안반데기)" aria-label="장소 검색" />
          </form>
          <button type="button" className={styles.here} onClick={here}>
            <LocateFixed strokeWidth={2} aria-hidden="true" />
            현재 위치
          </button>
          {notice && <p className={styles.notice}>{notice}</p>}
        </>
      )}
    </div>
  )
}
