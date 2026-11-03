import { DownloadOutlined, UploadOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Select, Space, Table, Tag, Typography, Upload } from 'antd'
import type { UploadFile } from 'antd'
import { useMemo, useState } from 'react'
import { usePermisiuni } from '../../api/interogari'
import { descarcaSablon, useActiveazaSablon, useIncarcaSablon, useSabloaneFormular, useTipuriFormular } from '../../api/formulare'
import type { SablonFormularApi } from '../../api/formulare'
import { AntetPagina } from '../../layout/AntetPagina'

export function Formulare() {
  const [codTip, setCodTip] = useState('comanda-furnizor')
  const [fisier, setFisier] = useState<File>()
  const { message } = App.useApp()
  const permisiuni = usePermisiuni()
  const tipuri = useTipuriFormular()
  const sabloane = useSabloaneFormular()
  const incarca = useIncarcaSablon()
  const activeaza = useActiveazaSablon()
  const poateEdita = permisiuni.data?.includes('administrare.editare') ?? false
  const tipSelectat = tipuri.data?.find((tip) => tip.cod === codTip)

  const etichete = useMemo(() => {
    if (!tipSelectat) return []
    return [
      ...tipSelectat.campuri.map((camp) => ({ cheie: camp.nume, text: camp.nume, obligatoriu: camp.obligatoriu })),
      ...tipSelectat.tabele.flatMap((tabel) =>
        tabel.coloane.map((camp) => ({
          cheie: `${tabel.nume}.${camp.nume}`,
          text: `${tabel.nume}.${camp.nume}`,
          obligatoriu: camp.obligatoriu,
        })),
      ),
    ]
  }, [tipSelectat])

  const incarcaFisier = async () => {
    if (!fisier) {
      message.warning('Alege un fișier Word .docx.')
      return
    }

    try {
      await incarca.mutateAsync({ codTip, fisier })
      setFisier(undefined)
      message.success('Șablonul a fost validat și adăugat ca versiune nouă.')
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Șablonul nu a putut fi încărcat.')
    }
  }

  const descarca = async (sablon: SablonFormularApi) => {
    try {
      const continut = await descarcaSablon(sablon.id)
      const url = URL.createObjectURL(continut)
      const link = document.createElement('a')
      link.href = url
      link.download = sablon.numeFisier
      link.click()
      URL.revokeObjectURL(url)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Șablonul nu a putut fi descărcat.')
    }
  }

  const schimbaVersiuneaActiva = async (sablon: SablonFormularApi) => {
    try {
      await activeaza.mutateAsync(sablon.id)
      message.success(`Versiunea ${sablon.versiune} este acum activă.`)
    } catch (eroare) {
      message.error(eroare instanceof Error ? eroare.message : 'Versiunea nu a putut fi activată.')
    }
  }

  return (
    <>
      <AntetPagina
        titlu="Formulare și documente"
        subtitlu="Administrează versiunile de șabloane Word folosite la generarea și importul documentelor."
        cale={[{ titlu: 'Acasă', la: '/' }, { titlu: 'Formulare și documente' }]}
      />

      <Card title="Șabloane Word">
        {tipuri.error && <Alert type="error" showIcon message="Tipurile de formular nu au putut fi încărcate." description={tipuri.error.message} />}
        {sabloane.error && <Alert type="error" showIcon message="Șabloanele nu au putut fi încărcate." description={sabloane.error.message} />}

        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            Fiecare încărcare creează o versiune nouă. Documentul „Necesar materiale” este recunoscut și pregătit automat
            pentru notele de comandă; pentru celelalte șabloane folosește controale Word etichetate. Originalele rămân
            disponibile pentru descărcare, iar activarea schimbă doar versiunea folosită la generare.
          </Typography.Paragraph>

          <Space wrap align="start">
            <Select
              aria-label="Tip formular"
              style={{ minWidth: 280 }}
              value={codTip}
              loading={tipuri.isLoading}
              options={tipuri.data?.map((tip) => ({ value: tip.cod, label: tip.denumire }))}
              onChange={(valoare) => setCodTip(valoare)}
            />
            <Upload
              accept=".docx"
              maxCount={1}
              fileList={fisier ? ([{ uid: fisier.name, name: fisier.name, status: 'done' }] as UploadFile[]) : []}
              beforeUpload={(f) => {
                setFisier(f)
                return false
              }}
              onRemove={() => {
                setFisier(undefined)
                return true
              }}
            >
              <Button icon={<UploadOutlined />}>Alege șablon .docx</Button>
            </Upload>
            <Button
              type="primary"
              icon={<UploadOutlined />}
              loading={incarca.isPending}
              disabled={!poateEdita || !fisier || !tipSelectat}
              onClick={incarcaFisier}
            >
              Validează și încarcă
            </Button>
          </Space>

          {tipSelectat && (
            <div>
              <Typography.Text strong>Etichete de conținut Word: </Typography.Text>
              <Space wrap size={[4, 4]} style={{ marginTop: 8 }}>
                {etichete.map((eticheta) => (
                  <Tag key={eticheta.cheie} color={eticheta.obligatoriu ? 'blue' : undefined}>
                    {eticheta.text}{eticheta.obligatoriu ? ' *' : ''}
                  </Tag>
                ))}
              </Space>
              <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
                Pentru rândurile repetate, pune etichetele de forma <code>linii.denumire</code> și <code>linii.cantitate</code>{' '}
                în controalele din același rând Word. Câmpurile marcate cu * sunt obligatorii.
              </Typography.Paragraph>
            </div>
          )}

          <Table<SablonFormularApi>
            rowKey="id"
            loading={sabloane.isLoading}
            dataSource={sabloane.data}
            pagination={{ pageSize: 10, hideOnSinglePage: true }}
            columns={[
              { title: 'Tip', dataIndex: 'codTip', render: (cod: string) => tipuri.data?.find((t) => t.cod === cod)?.denumire ?? cod },
              { title: 'Versiune', dataIndex: 'versiune', width: 100 },
              { title: 'Fișier', dataIndex: 'numeFisier', ellipsis: true },
              { title: 'SHA-256', dataIndex: 'hashSha256', ellipsis: true },
              { title: 'Stare', dataIndex: 'activ', width: 120, render: (activ: boolean) => <Tag color={activ ? 'green' : 'default'}>{activ ? 'Activ' : 'Arhivat'}</Tag> },
              {
                title: 'Acțiuni',
                key: 'actiuni',
                width: 190,
                render: (_, sablon) => (
                  <Space>
                    <Button type="link" icon={<DownloadOutlined />} onClick={() => descarca(sablon)}>Descarcă</Button>
                    {!sablon.activ && (
                      <Button type="link" disabled={!poateEdita} loading={activeaza.isPending} onClick={() => schimbaVersiuneaActiva(sablon)}>
                        Activează
                      </Button>
                    )}
                  </Space>
                ),
              },
            ]}
          />
        </Space>
      </Card>
    </>
  )
}
