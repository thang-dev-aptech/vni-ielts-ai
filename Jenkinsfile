pipeline {
    agent {
        label 'linux && docker && node24 && dotnet10'
    }

    options {
        timestamps()
        ansiColor('xterm')
        timeout(time: 75, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20', artifactNumToKeepStr: '10'))
        disableConcurrentBuilds(abortPrevious: true)
        skipDefaultCheckout(true)
    }

    parameters {
        booleanParam(
            name: 'RUN_BURN_IN',
            defaultValue: false,
            description: 'Run the idempotency flaky-test burn-in after verification.')
        booleanParam(
            name: 'RUN_FAILURE_DRILLS',
            defaultValue: false,
            description: 'Run live failure drills after verification.')
        string(
            name: 'PUBLIC_HOST',
            defaultValue: 'localhost',
            description: 'Hostname or IP users enter in their browser; no scheme or port.')
    }

    environment {
        CI = 'true'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
        // Verification requires a non-production signing value unique to this build.
        VNI_JWT_SIGNING_KEY = "jenkins-${env.BUILD_TAG}-verification-only"
        VNI_MONGODUMP = 'docker run --rm --network host mongo:7 mongodump'
        VNI_MONGORESTORE = 'docker run --rm -i --network host mongo:7 mongorestore'
        VNI_MONGOSH = 'docker exec -i vni-mongo mongosh'
        VNI_MONGOSH_URI = 'mongodb://localhost:27017/?directConnection=true'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                sh 'git status --short'
            }
        }

        stage('Verify agent toolchain') {
            steps {
                sh '''#!/usr/bin/env bash
                    set -euo pipefail
                    node --version
                    dotnet --version
                    docker --version
                    docker compose version
                    corepack enable
                    corepack prepare pnpm@10.15.0 --activate
                    pnpm --version
                    pnpm toolchain:check
                '''
            }
        }

        stage('Start local dependencies') {
            steps {
                sh '''#!/usr/bin/env bash
                    set -euo pipefail
                    docker compose -f infra/docker/compose.yaml -f infra/docker/compose.ci.yaml \
                      up -d --wait mongo minio pbm
                    docker compose -f infra/docker/compose.yaml -f infra/docker/compose.ci.yaml \
                      run --rm minio-init
                    bash scripts/pbm-setup.sh
                '''
            }
        }

        stage('Install dependencies') {
            steps {
                sh '''#!/usr/bin/env bash
                    set -euo pipefail
                    pnpm install --frozen-lockfile
                    pnpm --filter @vni/e2e exec playwright install-deps chromium
                    pnpm --filter @vni/e2e exec playwright install chromium
                '''
            }
        }

        stage('Foundation verification') {
            steps {
                sh '''#!/usr/bin/env bash
                    set -euo pipefail
                    # No --install: the previous stage already ran a
                    # frozen-lockfile install. verify.mjs's own install stage
                    # is opt-in for exactly the clean-checkout case this
                    # pipeline does not need a second time.
                    node scripts/verify.mjs
                '''
            }
        }

        stage('Flaky-test burn-in') {
            when {
                expression { return params.RUN_BURN_IN }
            }
            steps {
                sh 'node scripts/burn-in.mjs --suite=idempotency --iterations=10'
            }
        }

        stage('Failure drills') {
            when {
                expression { return params.RUN_FAILURE_DRILLS }
            }
            steps {
                sh 'node scripts/failure-drills.mjs --include-live'
            }
        }

        stage('Security reports and SBOM') {
            steps {
                sh '''#!/usr/bin/env bash
                    set -euo pipefail
                    node scripts/security-report.mjs
                    VNI_REQUIRE_DOCKER=1 bash scripts/sbom.sh
                '''
            }
        }

        stage('Build production images and deploy preview') {
            steps {
                sh '''#!/usr/bin/env bash
                    set -euo pipefail
                    export VNI_IMAGE_TAG="build-${BUILD_NUMBER}"
                    export VNI_API_URL="http://${PUBLIC_HOST}:5099"
                    export VNI_LEARNER_URL="http://${PUBLIC_HOST}:5173"
                    export VNI_ADMIN_URL="http://${PUBLIC_HOST}:5174"

                    docker compose -f infra/docker/compose.preview.yaml build
                    docker compose -f infra/docker/compose.preview.yaml up -d --remove-orphans --wait

                    # `--wait` verifies API and Worker healthchecks. Probe both
                    # Nginx frontends inside their containers because localhost
                    # in this shell is the Jenkins container, not the Docker host.
                    docker compose -f infra/docker/compose.preview.yaml exec -T web \
                      wget -q -O /dev/null http://127.0.0.1:8080/
                    docker compose -f infra/docker/compose.preview.yaml exec -T admin \
                      wget -q -O /dev/null http://127.0.0.1:8080/

                    printf 'Learner Web: http://%s:5173/\n' "${PUBLIC_HOST}"
                    printf 'Admin CMS:   http://%s:5174/\n' "${PUBLIC_HOST}"
                    printf 'API:         http://%s:5099/\n' "${PUBLIC_HOST}"
                '''
            }
        }
    }

    post {
        always {
            sh '''#!/usr/bin/env bash
                set +e
                mkdir -p _artifacts/smoke
                # No compose.production.yaml capture here: scripts/production-smoke.sh
                # (run inside the "Foundation verification" stage) owns that stack's
                # whole lifecycle and its own `trap cleanup EXIT` already tears it
                # down — and dumps the API log on failure — before this post block
                # ever runs. Capturing it here always produced an empty/erroring
                # compose.log, which archived as if it held something.
                docker compose -f infra/docker/compose.yaml logs --no-color \
                  > _artifacts/smoke/stack.log 2>&1
                # Do not stop MongoDB/MinIO here. The persistent preview deployed
                # by this pipeline uses them after the build has completed.
                exit 0
            '''
            archiveArtifacts artifacts: '''_artifacts/verify/**,
_artifacts/burn-in/**,
_artifacts/drills/**,
_artifacts/security/**,
_artifacts/sbom/**,
_artifacts/smoke/**,
e2e/test-results/**''', allowEmptyArchive: true, fingerprint: false
        }
    }
}
