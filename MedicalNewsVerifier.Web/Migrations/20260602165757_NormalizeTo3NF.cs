using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalNewsVerifier.Web.Migrations;

/// <inheritdoc />
public partial class NormalizeTo3NF : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "NewsSubmissions" (
                "Id" serial PRIMARY KEY,
                "Headline" character varying(500) NOT NULL,
                "NewsText" character varying(15000) NOT NULL,
                "SourceUrl" character varying(500) NULL,
                "ContentFingerprint" character varying(64) NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_NewsSubmissions_ContentFingerprint"
                ON "NewsSubmissions" ("ContentFingerprint");
            """);

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "SuspiciousFeatureKindDefinitions" (
                "Id" integer PRIMARY KEY,
                "Code" character varying(100) NOT NULL,
                "Title" character varying(200) NOT NULL,
                "Description" character varying(400) NOT NULL,
                "CssToken" character varying(50) NOT NULL
            );
            INSERT INTO "SuspiciousFeatureKindDefinitions" ("Id", "Code", "Title", "Description", "CssToken")
            VALUES
                (1, 'Emotional', 'Эмоционально окрашенная лексика', 'Текст содержит эмоциональную окраску и призван влиять на чувства.', 'emotional'),
                (2, 'Manipulative', 'Манипулятивные выражения', 'Выкристаллизованные словоформы, направленные на управление восприятием.', 'manipulative'),
                (3, 'Evaluative', 'Оценочная лексика', 'Слова с оценкой, создающие субъективное впечатление.', 'evaluative'),
                (4, 'UppercaseWord', 'Слова в верхнем регистре', 'Слова или фразы, полностью написанные заглавными буквами.', 'uppercase'),
                (5, 'Exclamation', 'Восклицательные знаки', 'Восклицательные знаки, усиливающие эмоциональность.', 'exclamation'),
                (6, 'Question', 'Вопросительные знаки', 'Вопросы, побуждающие к сомнению или уточнению.', 'question'),
                (7, 'Link', 'Ссылки', 'Адреса и гиперссылки в тексте.', 'source'),
                (8, 'Date', 'Даты', 'Упоминания дат и временных меток.', 'date'),
                (9, 'Number', 'Числовые данные', 'Числа и количественные обозначения.', 'number'),
                (10, 'SourceCue', 'Указание на источник', 'Упоминания источников и ссылок на авторитеты.', 'source'),
                (11, 'PythonHeuristic', 'Формулировки повышенного риска', 'Признаки, обнаруженные дополнительным Python-модулем.', 'python')
            ON CONFLICT ("Id") DO NOTHING;
            """);

        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "OfficialPublicationMatches" (
                "Id" serial PRIMARY KEY,
                "AnalysisRecordId" integer NOT NULL,
                "OfficialPublicationId" integer NOT NULL,
                "RelevanceScore" integer NOT NULL,
                "MatchedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT "FK_OfficialPublicationMatches_AnalysisRecords"
                    FOREIGN KEY ("AnalysisRecordId") REFERENCES "AnalysisRecords" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_OfficialPublicationMatches_OfficialPublications"
                    FOREIGN KEY ("OfficialPublicationId") REFERENCES "OfficialPublications" ("Id") ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS "IX_OfficialPublicationMatches_AnalysisRecordId"
                ON "OfficialPublicationMatches" ("AnalysisRecordId");
            CREATE INDEX IF NOT EXISTS "IX_OfficialPublicationMatches_OfficialPublicationId"
                ON "OfficialPublicationMatches" ("OfficialPublicationId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_OfficialPublicationMatches_AnalysisRecordId_OfficialPublicationId"
                ON "OfficialPublicationMatches" ("AnalysisRecordId", "OfficialPublicationId");
            """);

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'AnalysisRecords' AND column_name = 'Headline'
                ) THEN
                    INSERT INTO "NewsSubmissions" ("Headline", "NewsText", "SourceUrl", "ContentFingerprint")
                    SELECT DISTINCT ON (fp)
                        sub."Headline",
                        sub."NewsText",
                        sub."SourceUrl",
                        sub.fp
                    FROM (
                        SELECT
                            ar."Headline",
                            ar."NewsText",
                            ar."SourceUrl",
                            encode(
                                digest(
                                    lower(trim(regexp_replace(COALESCE(ar."Headline", ''), '\s+', ' ', 'g')))
                                    || E'\n' ||
                                    lower(trim(regexp_replace(COALESCE(ar."NewsText", ''), '\s+', ' ', 'g'))),
                                    'sha256'),
                                'hex') AS fp
                        FROM "AnalysisRecords" ar
                    ) sub
                    ON CONFLICT ("ContentFingerprint") DO NOTHING;

                    ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "NewsSubmissionId" integer NULL;

                    UPDATE "AnalysisRecords" ar
                    SET "NewsSubmissionId" = ns."Id"
                    FROM "NewsSubmissions" ns
                    WHERE ns."ContentFingerprint" = encode(
                        digest(
                            lower(trim(regexp_replace(COALESCE(ar."Headline", ''), '\s+', ' ', 'g')))
                            || E'\n' ||
                            lower(trim(regexp_replace(COALESCE(ar."NewsText", ''), '\s+', ' ', 'g'))),
                            'sha256'),
                        'hex');

                    ALTER TABLE "AnalysisRecords" DROP COLUMN IF EXISTS "Headline";
                    ALTER TABLE "AnalysisRecords" DROP COLUMN IF EXISTS "NewsText";
                    ALTER TABLE "AnalysisRecords" DROP COLUMN IF EXISTS "SourceUrl";
                    ALTER TABLE "AnalysisRecords" DROP COLUMN IF EXISTS "Status";

                    ALTER TABLE "AnalysisRecords" ALTER COLUMN "NewsSubmissionId" SET NOT NULL;
                END IF;
            END$$;
            """);

        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustedSources_Name" ON "TrustedSources" ("Name");

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = 'OfficialSources'
                ) THEN
                    INSERT INTO "TrustedSources" ("Name", "BaseUrl", "IsEnabled", "SortOrder")
                    SELECT os."Name", COALESCE(os."BaseUrl", ''), TRUE, 0
                    FROM "OfficialSources" os
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "TrustedSources" ts WHERE ts."Name" = os."Name"
                    );
                END IF;
            END$$;

            ALTER TABLE "OfficialPublications" ADD COLUMN IF NOT EXISTS "TrustedSourceId" integer NULL;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'OfficialPublications' AND column_name = 'OfficialSourceId'
                ) THEN
                    UPDATE "OfficialPublications" p
                    SET "TrustedSourceId" = ts."Id"
                    FROM "OfficialSources" os
                    INNER JOIN "TrustedSources" ts ON ts."Name" = os."Name"
                    WHERE p."OfficialSourceId" = os."Id" AND p."TrustedSourceId" IS NULL;
                ELSIF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'OfficialPublications' AND column_name = 'SourceName'
                ) THEN
                    INSERT INTO "TrustedSources" ("Name", "BaseUrl", "IsEnabled", "SortOrder")
                    SELECT DISTINCT p."SourceName", '', TRUE, 0
                    FROM "OfficialPublications" p
                    WHERE COALESCE(NULLIF(p."SourceName", ''), '') <> ''
                      AND NOT EXISTS (
                          SELECT 1 FROM "TrustedSources" ts WHERE ts."Name" = p."SourceName"
                      );

                    INSERT INTO "TrustedSources" ("Name", "BaseUrl", "IsEnabled", "SortOrder")
                    SELECT 'Unknown', '', TRUE, 0
                    WHERE EXISTS (
                        SELECT 1 FROM "OfficialPublications"
                        WHERE COALESCE(NULLIF("SourceName", ''), '') = ''
                    )
                      AND NOT EXISTS (SELECT 1 FROM "TrustedSources" WHERE "Name" = 'Unknown');

                    UPDATE "OfficialPublications" p
                    SET "TrustedSourceId" = ts."Id"
                    FROM "TrustedSources" ts
                    WHERE ts."Name" = COALESCE(NULLIF(p."SourceName", ''), 'Unknown')
                      AND p."TrustedSourceId" IS NULL;

                    ALTER TABLE "OfficialPublications" DROP COLUMN IF EXISTS "SourceName";
                END IF;
            END$$;

            INSERT INTO "TrustedSources" ("Name", "BaseUrl", "IsEnabled", "SortOrder")
            SELECT 'Unknown', '', TRUE, 0
            WHERE EXISTS (SELECT 1 FROM "OfficialPublications" WHERE "TrustedSourceId" IS NULL)
              AND NOT EXISTS (SELECT 1 FROM "TrustedSources" WHERE "Name" = 'Unknown');

            UPDATE "OfficialPublications"
            SET "TrustedSourceId" = (SELECT "Id" FROM "TrustedSources" WHERE "Name" = 'Unknown' LIMIT 1)
            WHERE "TrustedSourceId" IS NULL;

            ALTER TABLE "OfficialPublications" DROP COLUMN IF EXISTS "OfficialSourceId";
            DROP TABLE IF EXISTS "OfficialSources";

            ALTER TABLE "OfficialPublications" ALTER COLUMN "TrustedSourceId" SET NOT NULL;
            """);

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'FK_AnalysisRecords_NewsSubmissions_NewsSubmissionId'
                ) THEN
                    ALTER TABLE "AnalysisRecords"
                        ADD CONSTRAINT "FK_AnalysisRecords_NewsSubmissions_NewsSubmissionId"
                        FOREIGN KEY ("NewsSubmissionId") REFERENCES "NewsSubmissions" ("Id") ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'FK_OfficialPublications_TrustedSources_TrustedSourceId'
                ) THEN
                    ALTER TABLE "OfficialPublications"
                        ADD CONSTRAINT "FK_OfficialPublications_TrustedSources_TrustedSourceId"
                        FOREIGN KEY ("TrustedSourceId") REFERENCES "TrustedSources" ("Id") ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'FK_SuspiciousFragments_SuspiciousFeatureKindDefinitions_FeatureKind'
                ) THEN
                    ALTER TABLE "SuspiciousFragments"
                        ADD CONSTRAINT "FK_SuspiciousFragments_SuspiciousFeatureKindDefinitions_FeatureKind"
                        FOREIGN KEY ("FeatureKind") REFERENCES "SuspiciousFeatureKindDefinitions" ("Id") ON DELETE RESTRICT;
                END IF;
            END$$;

            CREATE INDEX IF NOT EXISTS "IX_AnalysisRecords_NewsSubmissionId" ON "AnalysisRecords" ("NewsSubmissionId");
            CREATE INDEX IF NOT EXISTS "IX_OfficialPublications_TrustedSourceId" ON "OfficialPublications" ("TrustedSourceId");
            CREATE INDEX IF NOT EXISTS "IX_SuspiciousFragments_FeatureKind" ON "SuspiciousFragments" ("FeatureKind");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "AnalysisRecords" DROP CONSTRAINT IF EXISTS "FK_AnalysisRecords_NewsSubmissions_NewsSubmissionId";
            ALTER TABLE "OfficialPublications" DROP CONSTRAINT IF EXISTS "FK_OfficialPublications_TrustedSources_TrustedSourceId";
            ALTER TABLE "SuspiciousFragments" DROP CONSTRAINT IF EXISTS "FK_SuspiciousFragments_SuspiciousFeatureKindDefinitions_FeatureKind";

            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "Headline" character varying(500) NOT NULL DEFAULT '';
            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "NewsText" character varying(15000) NOT NULL DEFAULT '';
            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "SourceUrl" character varying(500) NULL;
            ALTER TABLE "AnalysisRecords" ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 0;

            UPDATE "AnalysisRecords" ar
            SET "Headline" = ns."Headline",
                "NewsText" = ns."NewsText",
                "SourceUrl" = ns."SourceUrl"
            FROM "NewsSubmissions" ns
            WHERE ar."NewsSubmissionId" = ns."Id";

            ALTER TABLE "AnalysisRecords" DROP COLUMN IF EXISTS "NewsSubmissionId";

            ALTER TABLE "OfficialPublications" ADD COLUMN IF NOT EXISTS "SourceName" character varying(250) NOT NULL DEFAULT '';
            UPDATE "OfficialPublications" p
            SET "SourceName" = ts."Name"
            FROM "TrustedSources" ts
            WHERE p."TrustedSourceId" = ts."Id";

            ALTER TABLE "OfficialPublications" DROP COLUMN IF EXISTS "TrustedSourceId";

            DROP TABLE IF EXISTS "OfficialPublicationMatches";
            DROP TABLE IF EXISTS "NewsSubmissions";
            DROP TABLE IF EXISTS "SuspiciousFeatureKindDefinitions";
            """);
    }
}
